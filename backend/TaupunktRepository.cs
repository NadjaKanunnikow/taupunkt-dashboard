using System.Globalization;
using Npgsql;
using NpgsqlTypes;

namespace Taupunkt.Api;

public sealed class TaupunktRepository
{
    private const double DewPointMin = -40.0;
    private const double DewPointMax = 60.0;
    private readonly NpgsqlDataSource _db;

    public TaupunktRepository(NpgsqlDataSource db)
    {
        _db = db;
    }

    public async Task InitializeAsync()
    {
        await using var connection = await _db.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            CREATE TABLE IF NOT EXISTS control_settings
            (
                id integer PRIMARY KEY DEFAULT 1 CHECK (id = 1),
                mode text NOT NULL DEFAULT 'automatic' CHECK (mode IN ('automatic', 'manual')),
                manual_dew_point_difference_c double precision NULL,
                dew_point_diff_on double precision NOT NULL DEFAULT 4.0,
                dew_point_diff_off double precision NOT NULL DEFAULT 3.0,
                display_time text NULL,
                use_pi_time boolean NOT NULL DEFAULT true,
                updated_at timestamptz NOT NULL DEFAULT now()
            );

            INSERT INTO control_settings (id)
            VALUES (1)
            ON CONFLICT (id) DO NOTHING;

            CREATE TABLE IF NOT EXISTS measurements
            (
                id bigserial PRIMARY KEY,
                device_id text NOT NULL,
                measurement_location text NOT NULL CHECK (measurement_location IN ('inside', 'outside')),
                temperature double precision NOT NULL,
                humidity double precision NOT NULL,
                measured_at timestamptz NOT NULL,
                received_at timestamptz NOT NULL DEFAULT now(),
                dew_point_c double precision NOT NULL,
                dew_point_difference_c double precision NOT NULL,
                control_dew_point_difference_c double precision NOT NULL,
                manual_dew_point_difference_c double precision NULL,
                fan_on_threshold_c double precision NOT NULL,
                fan_off_threshold_c double precision NOT NULL,
                display_time text NULL,
                display_time_source text NOT NULL,
                fan_on boolean NOT NULL,
                control_mode text NOT NULL CHECK (control_mode IN ('automatic', 'manual'))
            );

            CREATE INDEX IF NOT EXISTS idx_measurements_location_time
                ON measurements (measurement_location, measured_at DESC, id DESC);

            CREATE INDEX IF NOT EXISTS idx_measurements_time
                ON measurements (measured_at DESC, id DESC);
            """, connection);

        await command.ExecuteNonQueryAsync();
    }


    public async Task<bool> CheckDatabaseAsync()
    {
        try
        {
            await using var connection = await _db.OpenConnectionAsync();
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<DashboardSnapshotForFrontendDto>> GetDashboardSnapshotsAsync(int take)
    {
        take = Math.Clamp(take, 1, 50);
        var sqlLimit = Math.Max(take * 6, 60);
        var measurements = new List<MeasurementDto>();

        await using var connection = await _db.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            SELECT
                id,
                device_id,
                measurement_location,
                temperature,
                humidity,
                measured_at,
                received_at,
                dew_point_c,
                dew_point_difference_c,
                control_dew_point_difference_c,
                manual_dew_point_difference_c,
                fan_on_threshold_c,
                fan_off_threshold_c,
                display_time,
                display_time_source,
                fan_on,
                control_mode
            FROM measurements
            ORDER BY measured_at DESC, id DESC
            LIMIT @limit;
            """, connection);
        command.Parameters.AddWithValue("limit", sqlLimit);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            measurements.Add(ReadMeasurement(reader));
        }

        return measurements
            .GroupBy(row => row.MeasuredAt)
            .OrderByDescending(group => group.Key)
            .Take(take)
            .Select(ToDashboardSnapshot)
            .OrderBy(snapshot => snapshot.MeasuredAt)
            .ToList();
    }

    public async Task<IReadOnlyList<HistoryRowDto>> GetHistoryRowsAsync(string metric, string? location, int limit)
    {
        limit = Math.Clamp(limit, 1, 10000);
        var normalizedMetric = NormalizeHistoryMetric(metric);
        var rows = new List<HistoryRowDto>();

        await using var connection = await _db.OpenConnectionAsync();

        if (normalizedMetric == "dewPointDifference")
        {
            await using var command = new NpgsqlCommand("""
                SELECT * FROM
                (
                    SELECT DISTINCT ON (measured_at)
                        measured_at,
                        dew_point_difference_c,
                        control_dew_point_difference_c,
                        manual_dew_point_difference_c,
                        fan_on_threshold_c,
                        fan_off_threshold_c,
                        display_time,
                        display_time_source,
                        fan_on,
                        control_mode
                    FROM measurements
                    ORDER BY measured_at DESC, id DESC
                    LIMIT @limit
                ) history
                ORDER BY measured_at DESC;
                """, connection);
            command.Parameters.AddWithValue("limit", limit);

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(new HistoryRowDto(
                    ReadDateTimeOffset(reader, "measured_at"),
                    normalizedMetric,
                    null,
                    null,
                    reader.GetDouble(reader.GetOrdinal("dew_point_difference_c")),
                    reader.GetDouble(reader.GetOrdinal("control_dew_point_difference_c")),
                    ReadNullableDouble(reader, "manual_dew_point_difference_c"),
                    reader.GetDouble(reader.GetOrdinal("fan_on_threshold_c")),
                    reader.GetDouble(reader.GetOrdinal("fan_off_threshold_c")),
                    ReadNullableString(reader, "display_time"),
                    reader.GetString(reader.GetOrdinal("display_time_source")),
                    reader.GetBoolean(reader.GetOrdinal("fan_on")),
                    reader.GetString(reader.GetOrdinal("control_mode"))));
            }

            return rows;
        }

        var normalizedLocation = NormalizeLocation(location);
        if (normalizedLocation is null)
        {
            throw new ArgumentException("location must be inside or outside for this metric.");
        }

        var valueColumn = normalizedMetric switch
        {
            "temperature" => "temperature",
            "humidity" => "humidity",
            "dewPoint" => "dew_point_c",
            _ => throw new ArgumentException("Unknown metric.")
        };

        await using var locationCommand = new NpgsqlCommand($"""
            SELECT
                measured_at,
                measurement_location,
                {valueColumn} AS value,
                fan_on,
                control_mode,
                fan_on_threshold_c,
                fan_off_threshold_c,
                display_time,
                display_time_source
            FROM measurements
            WHERE measurement_location = @location
            ORDER BY measured_at DESC, id DESC
            LIMIT @limit;
            """, connection);
        locationCommand.Parameters.AddWithValue("location", normalizedLocation);
        locationCommand.Parameters.AddWithValue("limit", limit);

        await using var locationReader = await locationCommand.ExecuteReaderAsync();
        while (await locationReader.ReadAsync())
        {
            rows.Add(new HistoryRowDto(
                ReadDateTimeOffset(locationReader, "measured_at"),
                normalizedMetric,
                locationReader.GetString(locationReader.GetOrdinal("measurement_location")),
                locationReader.GetDouble(locationReader.GetOrdinal("value")),
                null,
                null,
                null,
                locationReader.GetDouble(locationReader.GetOrdinal("fan_on_threshold_c")),
                locationReader.GetDouble(locationReader.GetOrdinal("fan_off_threshold_c")),
                ReadNullableString(locationReader, "display_time"),
                locationReader.GetString(locationReader.GetOrdinal("display_time_source")),
                locationReader.GetBoolean(locationReader.GetOrdinal("fan_on")),
                locationReader.GetString(locationReader.GetOrdinal("control_mode"))));
        }

        return rows;
    }

    public async Task<long> InsertMeasurementAsync(MeasurementRequest request)
    {
        var validation = ValidateMeasurement(request);
        if (validation is not null)
        {
            throw new ArgumentException(validation);
        }

        var location = request.MeasurementLocation!.Trim().ToLowerInvariant();
        var measuredAt = (request.MeasuredAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var mode = NormalizeMode(request.ControlMode) ?? "automatic";

        await using var connection = await _db.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO measurements
            (
                device_id,
                measurement_location,
                temperature,
                humidity,
                measured_at,
                dew_point_c,
                dew_point_difference_c,
                control_dew_point_difference_c,
                manual_dew_point_difference_c,
                fan_on_threshold_c,
                fan_off_threshold_c,
                display_time,
                display_time_source,
                fan_on,
                control_mode
            )
            VALUES
            (
                @device_id,
                @measurement_location,
                @temperature,
                @humidity,
                @measured_at,
                @dew_point_c,
                @dew_point_difference_c,
                @control_dew_point_difference_c,
                @manual_dew_point_difference_c,
                @fan_on_threshold_c,
                @fan_off_threshold_c,
                @display_time,
                @display_time_source,
                @fan_on,
                @control_mode
            )
            RETURNING id;
            """, connection);

        command.Parameters.AddWithValue("device_id", CleanText(request.DeviceId, "raspberry-pi"));
        command.Parameters.AddWithValue("measurement_location", location);
        command.Parameters.AddWithValue("temperature", request.Temperature!.Value);
        command.Parameters.AddWithValue("humidity", request.Humidity!.Value);
        command.Parameters.AddWithValue("measured_at", measuredAt);
        command.Parameters.AddWithValue("dew_point_c", request.DewPointC!.Value);
        command.Parameters.AddWithValue("dew_point_difference_c", request.DewPointDifferenceC!.Value);
        command.Parameters.AddWithValue("control_dew_point_difference_c", request.ControlDewPointDifferenceC!.Value);
        command.Parameters.Add(NullableDoubleParameter("manual_dew_point_difference_c", request.ManualDewPointDifferenceC));
        command.Parameters.AddWithValue("fan_on_threshold_c", request.FanOnThresholdC!.Value);
        command.Parameters.AddWithValue("fan_off_threshold_c", request.FanOffThresholdC!.Value);
        command.Parameters.Add(NullableTextParameter("display_time", NormalizeDisplayTime(request.DisplayTime)));
        command.Parameters.AddWithValue("display_time_source", CleanText(request.DisplayTimeSource, "raspberry-pi"));
        command.Parameters.AddWithValue("fan_on", request.FanOn ?? false);
        command.Parameters.AddWithValue("control_mode", mode);

        var id = await command.ExecuteScalarAsync();
        return Convert.ToInt64(id, CultureInfo.InvariantCulture);
    }

    public async Task<LatestMeasurementsResponse> GetLatestAsync(int take)
    {
        take = Math.Clamp(take, 1, 50);
        var inside = await ReadLatestLocationAsync("inside", take);
        var outside = await ReadLatestLocationAsync("outside", take);
        var snapshots = await ReadLatestSnapshotsAsync(take);
        return new LatestMeasurementsResponse(inside, outside, snapshots);
    }

    public async Task<object> GetHistoryAsync(string metric, int limit)
    {
        limit = Math.Clamp(limit, 1, 10000);
        var normalized = metric.Trim().ToLowerInvariant();

        if (normalized is "temperature" or "humidity" or "dew-point" or "dewpoint")
        {
            var column = normalized switch
            {
                "temperature" => "temperature",
                "humidity" => "humidity",
                _ => "dew_point_c"
            };
            var rows = await ReadSplitHistoryAsync(column, limit);
            return new SplitHistoryResponse(normalized, rows);
        }

        if (normalized is "dew-point-difference" or "dewpoint-difference" or "diff")
        {
            var rows = await ReadDifferenceHistoryAsync(limit);
            return new DifferenceHistoryResponse("dew-point-difference", rows);
        }

        throw new ArgumentException("Unknown metric. Use temperature, humidity, dew-point, or dew-point-difference.");
    }

    public async Task<ControlSettingsDto> GetControlAsync()
    {
        await using var connection = await _db.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            SELECT
                mode,
                manual_dew_point_difference_c,
                dew_point_diff_on,
                dew_point_diff_off,
                display_time,
                use_pi_time,
                updated_at
            FROM control_settings
            WHERE id = 1;
            """, connection);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("Control settings row is missing.");
        }

        return ReadControl(reader);
    }

    public async Task<ControlMergeResult> UpdateControlAsync(ControlUpdateRequest request)
    {
        var current = await GetControlAsync();
        var merged = MergeControl(current, request);
        if (merged.Error is not null)
        {
            return merged;
        }

        var settings = merged.Settings!;
        await using var connection = await _db.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            UPDATE control_settings
            SET
                mode = @mode,
                manual_dew_point_difference_c = @manual_dew_point_difference_c,
                dew_point_diff_on = @dew_point_diff_on,
                dew_point_diff_off = @dew_point_diff_off,
                display_time = @display_time,
                use_pi_time = @use_pi_time,
                updated_at = now()
            WHERE id = 1;
            """, connection);

        command.Parameters.AddWithValue("mode", settings.Mode);
        command.Parameters.Add(NullableDoubleParameter("manual_dew_point_difference_c", settings.ManualDewPointDifferenceC));
        command.Parameters.AddWithValue("dew_point_diff_on", settings.DewPointDiffOn);
        command.Parameters.AddWithValue("dew_point_diff_off", settings.DewPointDiffOff);
        command.Parameters.Add(NullableTextParameter("display_time", settings.DisplayTime));
        command.Parameters.AddWithValue("use_pi_time", settings.UsePiTime);
        await command.ExecuteNonQueryAsync();

        return new ControlMergeResult(await GetControlAsync(), null);
    }

    private async Task<IReadOnlyList<MeasurementDto>> ReadLatestLocationAsync(string location, int take)
    {
        var rows = new List<MeasurementDto>();
        await using var connection = await _db.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            SELECT * FROM
            (
                SELECT
                    id,
                    device_id,
                    measurement_location,
                    temperature,
                    humidity,
                    measured_at,
                    received_at,
                    dew_point_c,
                    dew_point_difference_c,
                    control_dew_point_difference_c,
                    manual_dew_point_difference_c,
                    fan_on_threshold_c,
                    fan_off_threshold_c,
                    display_time,
                    display_time_source,
                    fan_on,
                    control_mode
                FROM measurements
                WHERE measurement_location = @location
                ORDER BY measured_at DESC, id DESC
                LIMIT @take
            ) latest
            ORDER BY measured_at ASC, id ASC;
            """, connection);

        command.Parameters.AddWithValue("location", location);
        command.Parameters.AddWithValue("take", take);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(ReadMeasurement(reader));
        }

        return rows;
    }

    private async Task<IReadOnlyList<SnapshotDto>> ReadLatestSnapshotsAsync(int take)
    {
        var rows = new List<SnapshotDto>();
        await using var connection = await _db.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            SELECT * FROM
            (
                SELECT DISTINCT ON (measured_at)
                    measured_at,
                    dew_point_difference_c,
                    control_dew_point_difference_c,
                    manual_dew_point_difference_c,
                    fan_on_threshold_c,
                    fan_off_threshold_c,
                    display_time,
                    display_time_source,
                    fan_on,
                    control_mode
                FROM measurements
                ORDER BY measured_at DESC, id DESC
                LIMIT @take
            ) latest
            ORDER BY measured_at ASC;
            """, connection);

        command.Parameters.AddWithValue("take", take);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(ReadSnapshot(reader));
        }

        return rows;
    }

    private async Task<IReadOnlyList<SplitHistoryRow>> ReadSplitHistoryAsync(string column, int limit)
    {
        var rows = new List<SplitHistoryRow>();
        await using var connection = await _db.OpenConnectionAsync();
        await using var command = new NpgsqlCommand($"""
            SELECT
                measured_at,
                MAX({column}) FILTER (WHERE measurement_location = 'inside') AS inside,
                MAX({column}) FILTER (WHERE measurement_location = 'outside') AS outside
            FROM measurements
            GROUP BY measured_at
            ORDER BY measured_at DESC
            LIMIT @limit;
            """, connection);

        command.Parameters.AddWithValue("limit", limit);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new SplitHistoryRow(
                ReadDateTimeOffset(reader, "measured_at"),
                ReadNullableDouble(reader, "inside"),
                ReadNullableDouble(reader, "outside")));
        }

        return rows;
    }

    private async Task<IReadOnlyList<DifferenceHistoryRow>> ReadDifferenceHistoryAsync(int limit)
    {
        var rows = new List<DifferenceHistoryRow>();
        await using var connection = await _db.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            SELECT * FROM
            (
                SELECT DISTINCT ON (measured_at)
                    measured_at,
                    dew_point_difference_c,
                    control_dew_point_difference_c,
                    manual_dew_point_difference_c,
                    fan_on_threshold_c,
                    fan_off_threshold_c,
                    display_time,
                    display_time_source,
                    fan_on,
                    control_mode
                FROM measurements
                ORDER BY measured_at DESC, id DESC
                LIMIT @limit
            ) history
            ORDER BY measured_at DESC;
            """, connection);

        command.Parameters.AddWithValue("limit", limit);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new DifferenceHistoryRow(
                ReadDateTimeOffset(reader, "measured_at"),
                reader.GetDouble(reader.GetOrdinal("dew_point_difference_c")),
                reader.GetDouble(reader.GetOrdinal("control_dew_point_difference_c")),
                ReadNullableDouble(reader, "manual_dew_point_difference_c"),
                reader.GetDouble(reader.GetOrdinal("fan_on_threshold_c")),
                reader.GetDouble(reader.GetOrdinal("fan_off_threshold_c")),
                ReadNullableString(reader, "display_time"),
                reader.GetString(reader.GetOrdinal("display_time_source")),
                reader.GetBoolean(reader.GetOrdinal("fan_on")),
                reader.GetString(reader.GetOrdinal("control_mode"))));
        }

        return rows;
    }

    private static ControlMergeResult MergeControl(ControlSettingsDto current, ControlUpdateRequest request)
    {
        var requestedMode = NormalizeMode(request.Mode);
        if (request.Mode is not null && requestedMode is null)
        {
            return new ControlMergeResult(null, "mode must be automatic or manual.");
        }

        var mode = requestedMode ?? current.Mode;

        var onThreshold = FirstDouble(request.DewPointDiffOn, request.DewPointDifferenceOn, request.FanOnThresholdC)
            ?? current.DewPointDiffOn;
        var offThreshold = FirstDouble(request.DewPointDiffOff, request.DewPointDifferenceOff, request.FanOffThresholdC)
            ?? current.DewPointDiffOff;
        onThreshold = Clamp(onThreshold, DewPointMin, DewPointMax);
        offThreshold = Clamp(offThreshold, DewPointMin, DewPointMax);

        var manualDifference = current.ManualDewPointDifferenceC;
        var requestedManual = FirstDouble(
            request.ManualDewPointDifferenceC,
            request.ManualDewPointDiffC,
            request.ManualTaupunktDifferenceC,
            request.ManualTaupunktDiffC,
            request.DewPointDifferenceC,
            request.TaupunktDifferenceC,
            request.TaupunktDiffC);

        if (mode == "automatic")
        {
            manualDifference = null;
        }
        else if (requestedManual.HasValue)
        {
            manualDifference = Clamp(requestedManual.Value, DewPointMin, DewPointMax);
        }

        var requestedUsePiTime = request.UsePiTime ?? request.ResetDisplayTime ?? request.UseRaspberryPiTime;
        var usePiTime = requestedUsePiTime ?? current.UsePiTime;
        var displayTime = current.DisplayTime;
        var requestedDisplayTime = FirstText(request.DisplayTime, request.Time, request.ClockTime);

        if (usePiTime)
        {
            displayTime = null;
        }
        else if (requestedDisplayTime is not null)
        {
            displayTime = NormalizeDisplayTime(requestedDisplayTime);
            if (displayTime is null)
            {
                return new ControlMergeResult(null, "displayTime must use HH:mm format.");
            }
        }
        else if (requestedUsePiTime == false && displayTime is null)
        {
            displayTime = DateTime.UtcNow.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        var settings = new ControlSettingsDto(
            mode,
            manualDifference,
            onThreshold,
            offThreshold,
            onThreshold,
            offThreshold,
            displayTime,
            usePiTime,
            usePiTime ? "raspberry-pi" : "other",
            DateTimeOffset.UtcNow);

        return new ControlMergeResult(settings, null);
    }

    private static string? ValidateMeasurement(MeasurementRequest request)
    {
        var location = request.MeasurementLocation?.Trim().ToLowerInvariant();
        if (location is not ("inside" or "outside")) return "measurementLocation must be inside or outside.";
        if (request.Temperature is null) return "temperature is required.";
        if (request.Humidity is null) return "humidity is required.";
        if (request.DewPointC is null) return "dewPointC is required.";
        if (request.DewPointDifferenceC is null) return "dewPointDifferenceC is required.";
        if (request.ControlDewPointDifferenceC is null) return "controlDewPointDifferenceC is required.";
        if (request.FanOnThresholdC is null) return "fanOnThresholdC is required.";
        if (request.FanOffThresholdC is null) return "fanOffThresholdC is required.";

        var mode = NormalizeMode(request.ControlMode);
        if (request.ControlMode is not null && mode is null) return "controlMode must be automatic or manual.";

        return null;
    }

    private static MeasurementDto ReadMeasurement(NpgsqlDataReader reader)
    {
        return new MeasurementDto(
            reader.GetInt64(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("device_id")),
            reader.GetString(reader.GetOrdinal("measurement_location")),
            reader.GetDouble(reader.GetOrdinal("temperature")),
            reader.GetDouble(reader.GetOrdinal("humidity")),
            ReadDateTimeOffset(reader, "measured_at"),
            ReadDateTimeOffset(reader, "received_at"),
            reader.GetDouble(reader.GetOrdinal("dew_point_c")),
            reader.GetDouble(reader.GetOrdinal("dew_point_difference_c")),
            reader.GetDouble(reader.GetOrdinal("control_dew_point_difference_c")),
            ReadNullableDouble(reader, "manual_dew_point_difference_c"),
            reader.GetDouble(reader.GetOrdinal("fan_on_threshold_c")),
            reader.GetDouble(reader.GetOrdinal("fan_off_threshold_c")),
            ReadNullableString(reader, "display_time"),
            reader.GetString(reader.GetOrdinal("display_time_source")),
            reader.GetBoolean(reader.GetOrdinal("fan_on")),
            reader.GetString(reader.GetOrdinal("control_mode")));
    }

    private static SnapshotDto ReadSnapshot(NpgsqlDataReader reader)
    {
        return new SnapshotDto(
            ReadDateTimeOffset(reader, "measured_at"),
            reader.GetDouble(reader.GetOrdinal("dew_point_difference_c")),
            reader.GetDouble(reader.GetOrdinal("control_dew_point_difference_c")),
            ReadNullableDouble(reader, "manual_dew_point_difference_c"),
            reader.GetDouble(reader.GetOrdinal("fan_on_threshold_c")),
            reader.GetDouble(reader.GetOrdinal("fan_off_threshold_c")),
            ReadNullableString(reader, "display_time"),
            reader.GetString(reader.GetOrdinal("display_time_source")),
            reader.GetBoolean(reader.GetOrdinal("fan_on")),
            reader.GetString(reader.GetOrdinal("control_mode")));
    }


    private static DashboardSnapshotForFrontendDto ToDashboardSnapshot(IGrouping<DateTimeOffset, MeasurementDto> group)
    {
        var inside = group
            .Where(row => row.MeasurementLocation == "inside")
            .OrderByDescending(row => row.ReceivedAt)
            .ThenByDescending(row => row.Id)
            .FirstOrDefault();
        var outside = group
            .Where(row => row.MeasurementLocation == "outside")
            .OrderByDescending(row => row.ReceivedAt)
            .ThenByDescending(row => row.Id)
            .FirstOrDefault();
        var metadata = group
            .OrderByDescending(row => row.ReceivedAt)
            .ThenByDescending(row => row.Id)
            .First();

        return new DashboardSnapshotForFrontendDto(
            metadata.MeasuredAt,
            inside is null ? null : new LocationReadingDto(inside.Temperature, inside.Humidity, inside.DewPointC),
            outside is null ? null : new LocationReadingDto(outside.Temperature, outside.Humidity, outside.DewPointC),
            metadata.DewPointDifferenceC,
            metadata.ControlDewPointDifferenceC,
            metadata.ManualDewPointDifferenceC,
            metadata.FanOnThresholdC,
            metadata.FanOffThresholdC,
            metadata.DisplayTime,
            metadata.DisplayTimeSource,
            metadata.FanOn,
            metadata.ControlMode);
    }

    private static string? NormalizeLocation(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return null;
        var normalized = location.Trim().ToLowerInvariant();
        return normalized is "inside" or "outside" ? normalized : null;
    }

    private static string NormalizeHistoryMetric(string metric)
    {
        var normalized = metric.Trim().Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
        return normalized switch
        {
            "temperature" or "temp" => "temperature",
            "humidity" => "humidity",
            "dewpoint" or "taupunkt" => "dewPoint",
            "dewpointdifference" or "taupunktdifference" or "taupunktdiff" or "diff" => "dewPointDifference",
            _ => throw new ArgumentException("Unknown metric. Use temperature, humidity, dewPoint, or dewPointDifference.")
        };
    }

    private static ControlSettingsDto ReadControl(NpgsqlDataReader reader)
    {
        var usePiTime = reader.GetBoolean(reader.GetOrdinal("use_pi_time"));
        var onThreshold = reader.GetDouble(reader.GetOrdinal("dew_point_diff_on"));
        var offThreshold = reader.GetDouble(reader.GetOrdinal("dew_point_diff_off"));
        return new ControlSettingsDto(
            reader.GetString(reader.GetOrdinal("mode")),
            ReadNullableDouble(reader, "manual_dew_point_difference_c"),
            onThreshold,
            offThreshold,
            onThreshold,
            offThreshold,
            ReadNullableString(reader, "display_time"),
            usePiTime,
            usePiTime ? "raspberry-pi" : "other",
            ReadDateTimeOffset(reader, "updated_at"));
    }

    private static DateTimeOffset ReadDateTimeOffset(NpgsqlDataReader reader, string name)
    {
        var dateTime = reader.GetDateTime(reader.GetOrdinal(name));
        if (dateTime.Kind == DateTimeKind.Unspecified)
        {
            dateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
        }

        return new DateTimeOffset(dateTime.ToUniversalTime());
    }

    private static string? ReadNullableString(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static double? ReadNullableDouble(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
    }

    private static string? NormalizeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return null;
        var normalized = mode.Trim().ToLowerInvariant();
        return normalized is "automatic" or "manual" ? normalized : null;
    }

    private static string? NormalizeDisplayTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        if (TimeOnly.TryParseExact(
                value.Trim(),
                new[] { "HH:mm", "H:mm", "HH:mm:ss" },
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var time))
        {
            return time.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static string CleanText(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static NpgsqlParameter NullableDoubleParameter(string name, double? value)
    {
        return new NpgsqlParameter(name, NpgsqlDbType.Double)
        {
            Value = value.HasValue ? value.Value : DBNull.Value
        };
    }

    private static NpgsqlParameter NullableTextParameter(string name, string? value)
    {
        return new NpgsqlParameter(name, NpgsqlDbType.Text)
        {
            Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim()
        };
    }

    private static double Clamp(double value, double minimum, double maximum)
    {
        return Math.Max(minimum, Math.Min(maximum, value));
    }

    private static double? FirstDouble(params double?[] values)
    {
        return values.FirstOrDefault(value => value.HasValue);
    }

    private static string? FirstText(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
