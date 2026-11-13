using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Manajoy.GameClass.Inspector.Configuration;
using Manajoy.GameClass.Inspector.Infrastructure;
using Manajoy.GameClass.Inspector.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Manajoy.GameClass.Inspector.Output;

/// <summary>Writes output/inspection-YYYYMMDD-HHmmss.json. The result model holds no connection strings or passwords.</summary>
internal sealed class JsonReporter(IOptions<InspectorOptions> options, ILogger<JsonReporter> logger)
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        // Keep Cyrillic table names readable in the file; the output is never embedded in HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper) },
    };

    public async Task<string> WriteAsync(InspectionResult result, CancellationToken cancellationToken)
    {
        var directory = AppPaths.Resolve(options.Value.OutputDirectory);
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"inspection-{result.Environment.Timestamp:yyyyMMdd-HHmmss}.json");
        await using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            await JsonSerializer.SerializeAsync(stream, result, SerializerOptions, cancellationToken);
        }

        logger.LogInformation("JSON report written to {Path}", path);
        return path;
    }
}
