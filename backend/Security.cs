namespace Taupunkt.Api;

public static class Security
{
    public static bool HasPiAccess(HttpRequest request)
    {
        var expected = FirstConfigured("API_KEY", "APP_API_KEY");
        if (string.IsNullOrWhiteSpace(expected))
        {
            return true;
        }

        return HeaderEquals(request, "X-API-Key", expected);
    }

    public static bool HasAdminAccess(HttpRequest request)
    {
        var expected = FirstConfigured("ADMIN_TOKEN", "APP_API_KEY", "API_KEY");
        if (string.IsNullOrWhiteSpace(expected))
        {
            return true;
        }

        return HeaderEquals(request, "X-Admin-Token", expected)
            || HeaderEquals(request, "X-API-Key", expected);
    }

    private static string? FirstConfigured(params string[] names)
    {
        foreach (var name in names)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool HeaderEquals(HttpRequest request, string headerName, string expected)
    {
        return request.Headers.TryGetValue(headerName, out var actual)
            && string.Equals(actual.ToString(), expected, StringComparison.Ordinal);
    }
}
