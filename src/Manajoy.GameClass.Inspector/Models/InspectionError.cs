namespace Manajoy.GameClass.Inspector.Models;

public enum ErrorSeverity
{
    Warning,
    Error,
}

public sealed record InspectionError(
    string Stage,
    string? Target,
    string Message,
    ErrorSeverity Severity,
    DateTimeOffset Timestamp);

public static class InspectionStage
{
    public const string InstanceDiscovery = "instance-discovery";
    public const string ConnectionProbe = "connection-probe";
    public const string DatabaseDiscovery = "database-discovery";
    public const string SchemaInspection = "schema-inspection";
    public const string SampleRows = "sample-rows";
    public const string Output = "output";
}
