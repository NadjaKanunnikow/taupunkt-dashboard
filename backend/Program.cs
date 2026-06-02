using System.Text.Json;
using Npgsql;
using Taupunkt.Api;

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        var origins = (Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(origin => origin.TrimEnd('/'))
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .ToArray();

        if (origins.Length == 0)
        {
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        }
        else
        {
            policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
        }
    });
});

builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(DatabaseConnection.Build()));
builder.Services.AddSingleton<TaupunktRepository>();

var app = builder.Build();

app.UseCors("frontend");
app.UseDefaultFiles();
app.UseStaticFiles();

await app.Services.GetRequiredService<TaupunktRepository>().InitializeAsync();

app.MapGet("/", () => Results.Ok(new
{
    service = "taupunkt-api",
    status = "GOOD",
    endpoints = new[]
    {
        "GET /health",
        "POST /api/measurements",
        "GET /api/control",
        "PATCH /api/control",
        "GET /api/measurements/latest?take=10",
        "GET /api/measurements/history?metric=temperature"
    }
}));

app.MapGet("/health", () => Results.Ok(new
{
    status = "GOOD",
    checkedAt = DateTimeOffset.UtcNow
}));

app.MapGet("/api/status/health", async (TaupunktRepository repository) =>
{
    var databaseOk = await repository.CheckDatabaseAsync();
    return Results.Ok(new
    {
        ok = databaseOk,
        database = databaseOk ? "ok" : "error",
        apiKeyRequired = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("APP_API_KEY"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("API_KEY"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ADMIN_TOKEN")),
        utcNow = DateTimeOffset.UtcNow
    });
});

app.MapPost("/api/measurements", async (HttpRequest http, TaupunktRepository repository, MeasurementRequest request) =>
{
    if (!Security.HasPiAccess(http))
    {
        return Results.Unauthorized();
    }

    try
    {
        var id = await repository.InsertMeasurementAsync(request);
        return Results.Created($"/api/measurements/{id}", new { id });
    }
    catch (ArgumentException error)
    {
        return Results.BadRequest(new { error = error.Message });
    }
});

app.MapGet("/api/measurements/latest", async (TaupunktRepository repository, int? take) =>
{
    var response = await repository.GetLatestAsync(take ?? 10);
    return Results.Ok(response);
});

app.MapGet("/api/dashboard/latest", async (TaupunktRepository repository, int? take) =>
{
    var response = await repository.GetDashboardSnapshotsAsync(take ?? 10);
    return Results.Ok(response);
});

app.MapGet("/api/measurements/history", async (TaupunktRepository repository, string metric, int? limit) =>
{
    try
    {
        var response = await repository.GetHistoryAsync(metric, limit ?? 1000);
        return Results.Ok(response);
    }
    catch (ArgumentException error)
    {
        return Results.BadRequest(new { error = error.Message });
    }
});

app.MapGet("/api/history/{metric}", async (TaupunktRepository repository, string metric, string? location, int? limit) =>
{
    try
    {
        var response = await repository.GetHistoryRowsAsync(metric, location, limit ?? 10000);
        return Results.Ok(response);
    }
    catch (ArgumentException error)
    {
        return Results.BadRequest(new { error = error.Message });
    }
});

app.MapGet("/api/control", async (TaupunktRepository repository) =>
{
    var settings = await repository.GetControlAsync();
    return Results.Ok(settings);
});

app.MapPatch("/api/control", async (HttpRequest http, TaupunktRepository repository, ControlUpdateRequest request) =>
{
    if (!Security.HasAdminAccess(http))
    {
        return Results.Unauthorized();
    }

    var result = await repository.UpdateControlAsync(request);
    if (result.Error is not null)
    {
        return Results.BadRequest(new { error = result.Error });
    }

    return Results.Ok(result.Settings);
});

app.MapPut("/api/control", async (HttpRequest http, TaupunktRepository repository, ControlUpdateRequest request) =>
{
    if (!Security.HasAdminAccess(http))
    {
        return Results.Unauthorized();
    }

    var result = await repository.UpdateControlAsync(request);
    if (result.Error is not null)
    {
        return Results.BadRequest(new { error = result.Error });
    }

    return Results.Ok(result.Settings);
});

app.MapFallbackToFile("index.html");

await app.RunAsync();
