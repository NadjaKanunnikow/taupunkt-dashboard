namespace Taupunkt.Api;

public sealed record MeasurementRequest(
    string? DeviceId,
    string? MeasurementLocation,
    double? Temperature,
    double? Humidity,
    DateTimeOffset? MeasuredAt,
    double? DewPointC,
    double? DewPointDifferenceC,
    double? ControlDewPointDifferenceC,
    double? ManualDewPointDifferenceC,
    double? FanOnThresholdC,
    double? FanOffThresholdC,
    string? DisplayTime,
    string? DisplayTimeSource,
    bool? FanOn,
    string? ControlMode
);

public sealed record MeasurementDto(
    long Id,
    string DeviceId,
    string MeasurementLocation,
    double Temperature,
    double Humidity,
    DateTimeOffset MeasuredAt,
    DateTimeOffset ReceivedAt,
    double DewPointC,
    double DewPointDifferenceC,
    double ControlDewPointDifferenceC,
    double? ManualDewPointDifferenceC,
    double FanOnThresholdC,
    double FanOffThresholdC,
    string? DisplayTime,
    string DisplayTimeSource,
    bool FanOn,
    string ControlMode
);

public sealed record SnapshotDto(
    DateTimeOffset MeasuredAt,
    double DewPointDifferenceC,
    double ControlDewPointDifferenceC,
    double? ManualDewPointDifferenceC,
    double FanOnThresholdC,
    double FanOffThresholdC,
    string? DisplayTime,
    string DisplayTimeSource,
    bool FanOn,
    string ControlMode
);

public sealed record LatestMeasurementsResponse(
    IReadOnlyList<MeasurementDto> Inside,
    IReadOnlyList<MeasurementDto> Outside,
    IReadOnlyList<SnapshotDto> Snapshots
);

public sealed record SplitHistoryRow(
    DateTimeOffset MeasuredAt,
    double? Inside,
    double? Outside
);

public sealed record DifferenceHistoryRow(
    DateTimeOffset MeasuredAt,
    double DewPointDifferenceC,
    double ControlDewPointDifferenceC,
    double? ManualDewPointDifferenceC,
    double FanOnThresholdC,
    double FanOffThresholdC,
    string? DisplayTime,
    string DisplayTimeSource,
    bool FanOn,
    string ControlMode
);

public sealed record SplitHistoryResponse(
    string Metric,
    IReadOnlyList<SplitHistoryRow> Rows
);

public sealed record DifferenceHistoryResponse(
    string Metric,
    IReadOnlyList<DifferenceHistoryRow> Rows
);


public sealed record LocationReadingDto(
    double Temperature,
    double Humidity,
    double DewPointC
);

public sealed record DashboardSnapshotForFrontendDto(
    DateTimeOffset MeasuredAt,
    LocationReadingDto? Inside,
    LocationReadingDto? Outside,
    double? DewPointDifferenceC,
    double? ControlDewPointDifferenceC,
    double? ManualDewPointDifferenceC,
    double? FanOnThresholdC,
    double? FanOffThresholdC,
    string? DisplayTime,
    string? DisplayTimeSource,
    bool? FanOn,
    string? ControlMode
);

public sealed record HistoryRowDto(
    DateTimeOffset MeasuredAt,
    string Metric,
    string? Location,
    double? Value,
    double? DewPointDifferenceC,
    double? ControlDewPointDifferenceC,
    double? ManualDewPointDifferenceC,
    double? FanOnThresholdC,
    double? FanOffThresholdC,
    string? DisplayTime,
    string? DisplayTimeSource,
    bool FanOn,
    string ControlMode
);

public sealed record ControlSettingsDto(
    string Mode,
    double? ManualDewPointDifferenceC,
    double DewPointDiffOn,
    double DewPointDiffOff,
    double FanOnThresholdC,
    double FanOffThresholdC,
    string? DisplayTime,
    bool UsePiTime,
    string DisplayTimeSource,
    DateTimeOffset UpdatedAt
);

public sealed record ControlUpdateRequest(
    string? Mode,
    double? ManualDewPointDifferenceC,
    double? ManualDewPointDiffC,
    double? ManualTaupunktDifferenceC,
    double? ManualTaupunktDiffC,
    double? DewPointDifferenceC,
    double? TaupunktDifferenceC,
    double? TaupunktDiffC,
    double? DewPointDiffOn,
    double? DewPointDifferenceOn,
    double? FanOnThresholdC,
    double? DewPointDiffOff,
    double? DewPointDifferenceOff,
    double? FanOffThresholdC,
    string? DisplayTime,
    string? Time,
    string? ClockTime,
    bool? UsePiTime,
    bool? ResetDisplayTime,
    bool? UseRaspberryPiTime
);

public sealed record ControlMergeResult(ControlSettingsDto? Settings, string? Error);
