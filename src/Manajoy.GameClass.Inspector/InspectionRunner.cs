using Manajoy.GameClass.Inspector.Analysis;
using Manajoy.GameClass.Inspector.Configuration;
using Manajoy.GameClass.Inspector.Discovery;
using Manajoy.GameClass.Inspector.Infrastructure;
using Manajoy.GameClass.Inspector.Inspection;
using Manajoy.GameClass.Inspector.Models;
using Manajoy.GameClass.Inspector.Output;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Manajoy.GameClass.Inspector;

/// <summary>
/// One complete read-only inspection: environment → instances → connections → databases → schemas
/// → candidate scoring → optional samples → reports. Each stage tolerates failures of the previous ones.
/// </summary>
internal sealed class InspectionRunner(
    EnvironmentInfoProvider environmentProvider,
    SqlInstanceDiscovery instanceDiscovery,
    ConnectionProbe connectionProbe,
    DatabaseDiscovery databaseDiscovery,
    SchemaInspector schemaInspector,
    GameClassCandidateScorer candidateScorer,
    SampleRowInspector sampleRowInspector,
    ConsoleReporter consoleReporter,
    JsonReporter jsonReporter,
    InspectionErrors errors,
    IOptions<InspectorOptions> options,
    ILogger<InspectionRunner> logger)
{
    public async Task<InspectionResult> RunAsync(CancellationToken cancellationToken)
    {
        var environment = environmentProvider.Collect();
        consoleReporter.PrintHeader(environment);
        logger.LogInformation("Starting {Mode} inspection on {Machine} as {User}", environment.InspectionMode, environment.MachineName, environment.CurrentUser);

        var instances = new List<SqlInstanceInfo>();
        var connected = new List<ConnectedInstance>();
        foreach (var instance in instanceDiscovery.Discover(cancellationToken))
        {
            var outcome = await connectionProbe.ProbeAsync(instance, environment.IsElevated, cancellationToken);
            var probed = instance with { Connection = outcome.Result };
            instances.Add(probed);
            if (outcome.Target is not null)
            {
                connected.Add(new ConnectedInstance(probed, outcome.Target));
            }
        }

        var databases = new List<DatabaseInfo>();
        var schemas = new List<DatabaseSchema>();
        var candidates = new List<GameClassCandidate>();
        foreach (var instance in connected)
        {
            await InspectInstanceAsync(instance, databases, schemas, candidates, cancellationToken);
        }

        var rankedCandidates = candidates
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenByDescending(candidate => candidate.Score)
            .ToList();
        var reportedSchemas = schemas
            .Where(schema => rankedCandidates.Any(candidate =>
                candidate.Server == schema.Server && candidate.Database == schema.Database && candidate.Confidence >= CandidateConfidence.Low))
            .ToList();

        IReadOnlyList<SampleRowsResult>? samples = null;
        if (options.Value.AllowSampleRows)
        {
            samples = await sampleRowInspector.CollectAsync(connected, schemas, cancellationToken);
        }

        var result = new InspectionResult
        {
            Environment = environment,
            SqlInstances = instances,
            Databases = databases,
            GameClassCandidates = rankedCandidates,
            Schemas = reportedSchemas,
            SampleRows = samples,
            Errors = errors.Snapshot(),
        };

        var reportPath = await TryWriteJsonAsync(result, cancellationToken);
        if (reportPath is null)
        {
            result = result with { Errors = errors.Snapshot() };
        }

        logger.LogInformation("Inspection completed with {Count} warning(s)/error(s)", result.Errors.Count);
        consoleReporter.PrintReport(result, reportPath, AppPaths.LogFilePath(options.Value.LogDirectory, DateTime.Now));
        return result;
    }

    private async Task InspectInstanceAsync(
        ConnectedInstance instance,
        List<DatabaseInfo> databases,
        List<DatabaseSchema> schemas,
        List<GameClassCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var instanceDatabases = await databaseDiscovery.DiscoverAsync(instance, cancellationToken);
        databases.AddRange(instanceDatabases);

        foreach (var database in instanceDatabases.Where(database => database.IsAccessible))
        {
            try
            {
                var schema = await schemaInspector.InspectAsync(instance, database, cancellationToken);
                schemas.Add(schema);
                candidates.Add(candidateScorer.Score(schema));
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Unexpected failure while inspecting {Database} on {Server}", database.DatabaseName, instance.ServerName);
                errors.Error(InspectionStage.SchemaInspection, $"{instance.ServerName}/{database.DatabaseName}", ex.Message);
            }
        }
    }

    private async Task<string?> TryWriteJsonAsync(InspectionResult result, CancellationToken cancellationToken)
    {
        try
        {
            return await jsonReporter.WriteAsync(result, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Cannot write the JSON report");
            errors.Error(InspectionStage.Output, options.Value.OutputDirectory, $"Cannot write JSON report: {ex.Message}");
            return null;
        }
    }
}
