using System.Globalization;
using Manajoy.GameClass.Inspector.Discovery;
using Manajoy.GameClass.Inspector.Models;

namespace Manajoy.GameClass.Inspector.Output;

/// <summary>Human-readable report for the operator's console window.</summary>
internal sealed class ConsoleReporter
{
    private const string Ok = "✓";
    private const string Fail = "✗";
    private const string Separator = "============================================================";
    private const int MaxErrorsShown = 20;

    private readonly TextWriter _out = Console.Out;

    public void PrintHeader(EnvironmentInfo environment)
    {
        _out.WriteLine();
        _out.WriteLine(Separator);
        _out.WriteLine($" {environment.ApplicationName} v{environment.ApplicationVersion}");
        _out.WriteLine(Separator);
        _out.WriteLine($"Machine:         {environment.MachineName}");
        _out.WriteLine($"OS:              {environment.OsName} ({environment.OsVersion})");
        _out.WriteLine($"Architecture:    {environment.OsArchitecture} (process {environment.ProcessArchitecture})");
        _out.WriteLine($".NET:            {environment.DotNetVersion}");
        _out.WriteLine($"User:            {environment.CurrentUser}{FormatElevation(environment.IsElevated)}");
        _out.WriteLine($"Timestamp:       {environment.Timestamp.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)}");
        _out.WriteLine();
        _out.WriteLine($"Inspection mode: {environment.InspectionMode}");
        _out.WriteLine();
    }

    public void PrintReport(InspectionResult result, string? reportPath, string logPath)
    {
        PrintInstances(result.SqlInstances);
        PrintDatabases(result.Databases);
        PrintCandidates(result.GameClassCandidates);
        PrintSamples(result.SampleRows);
        PrintErrors(result.Errors);
        PrintSummary(result, reportPath, logPath);
    }

    private void PrintInstances(IReadOnlyList<SqlInstanceInfo> instances)
    {
        Section($"SQL SERVER INSTANCES ({instances.Count})");
        if (instances.Count == 0)
        {
            _out.WriteLine("  No local SQL Server / MSDE instance was found.");
            _out.WriteLine();
            return;
        }

        for (var i = 0; i < instances.Count; i++)
        {
            var instance = instances[i];
            _out.WriteLine($"[{i + 1}]");
            _out.WriteLine($"  Instance: {instance.ServerName}");
            _out.WriteLine($"  Service:  {instance.ServiceName ?? "(unknown)"}");
            _out.WriteLine($"  Status:   {instance.ServiceStatus ?? "(unknown)"}");
            if (instance.Version is not null)
            {
                _out.WriteLine($"  Version:  {instance.Version} {Parenthesize(SqlVersion.FamilyName(instance.RegistryMajorVersion))}(registry)");
            }

            _out.WriteLine($"  Found by: {string.Join(", ", instance.DiscoverySources)}");
            PrintConnection(instance.Connection);
            _out.WriteLine();
        }
    }

    private void PrintConnection(ConnectionProbeResult? connection)
    {
        if (connection is null)
        {
            return;
        }

        if (connection.Success)
        {
            _out.WriteLine($"  {Ok} Connection successful ({connection.Provider}, {connection.Authentication} authentication)");
            var server = connection.Server;
            if (server is not null)
            {
                _out.WriteLine($"    Server:  {server.ServerName} — {server.ProductFamily} {server.ProductVersion} {server.ProductLevel}".TrimEnd());
                _out.WriteLine($"    Edition: {server.Edition}");
                _out.WriteLine($"    Login:   {server.CurrentLogin}");
            }

            return;
        }

        _out.WriteLine($"  {Fail} Connection failed: {connection.FailureReason}");
        foreach (var attempt in connection.Attempts)
        {
            _out.WriteLine($"    - {attempt.Provider} / {attempt.Authentication}: {(attempt.Succeeded ? "ok" : attempt.Error)}");
        }

        if (connection.Hint is not null)
        {
            _out.WriteLine($"    Hint: {connection.Hint}");
        }
    }

    private void PrintDatabases(IReadOnlyList<DatabaseInfo> databases)
    {
        Section($"DATABASES ({databases.Count} user database(s))");
        foreach (var group in databases.GroupBy(database => database.Server))
        {
            _out.WriteLine($"  {group.Key}");
            foreach (var database in group)
            {
                var size = database.SizeMb is null ? "?" : $"{database.SizeMb.Value.ToString("0.#", CultureInfo.InvariantCulture)} MB";
                var access = database.IsAccessible ? $"{Ok} accessible" : $"{Fail} {database.AccessError}";
                _out.WriteLine($"    {database.DatabaseName,-28} {database.State ?? "?",-8} compat {database.CompatibilityLevel?.ToString(CultureInfo.InvariantCulture) ?? "?",-4} {size,10}  {access}");
            }
        }

        _out.WriteLine();
    }

    private void PrintCandidates(IReadOnlyList<GameClassCandidate> candidates)
    {
        Section("GAMECLASS DATABASE CANDIDATES (heuristic)");
        var relevant = candidates.Where(candidate => candidate.Confidence > CandidateConfidence.None).ToList();
        if (relevant.Count == 0)
        {
            _out.WriteLine("  No database resembles a gaming-club schema.");
            _out.WriteLine();
            return;
        }

        foreach (var candidate in relevant)
        {
            _out.WriteLine($"Database: {candidate.Database}  ({candidate.Server})");
            _out.WriteLine($"GameClass confidence: {candidate.Confidence.ToString().ToUpperInvariant()} (score {candidate.Score})");
            _out.WriteLine($"Tables: {candidate.TableCount}   Views: {candidate.ViewCount}   Procedures/functions: {candidate.RoutineCount}");

            if (candidate.Reasons.Count > 0)
            {
                _out.WriteLine("Reasons:");
                foreach (var reason in candidate.Reasons)
                {
                    _out.WriteLine($"  - {reason}");
                }
            }

            if (candidate.InterestingStructures.Count > 0)
            {
                _out.WriteLine();
                _out.WriteLine("Interesting structures:");
                foreach (var structure in candidate.InterestingStructures)
                {
                    var rows = structure.ApproximateRowCount is null ? string.Empty : $"  (~{structure.ApproximateRowCount} rows)";
                    _out.WriteLine();
                    _out.WriteLine($"  {structure.Category}-like {structure.ObjectType}: {structure.ObjectName}{rows}");
                    foreach (var column in structure.Columns)
                    {
                        _out.WriteLine($"    {column}");
                    }

                    if (structure.OtherMatches.Count > 0)
                    {
                        _out.WriteLine($"    (also: {string.Join(", ", structure.OtherMatches)})");
                    }
                }
            }

            _out.WriteLine();
        }

        var unrelated = candidates.Count - relevant.Count;
        if (unrelated > 0)
        {
            _out.WriteLine($"({unrelated} other accessible database(s) show no gaming-club signals.)");
            _out.WriteLine();
        }
    }

    private void PrintSamples(IReadOnlyList<SampleRowsResult>? samples)
    {
        if (samples is null)
        {
            return;
        }

        Section($"SAMPLE ROWS ({samples.Count} table(s), sensitive columns masked)");
        foreach (var sample in samples)
        {
            _out.WriteLine($"  {sample.Server}/{sample.Database}/{sample.Table}");
            if (sample.MaskedColumns.Count > 0)
            {
                _out.WriteLine($"    Masked: {string.Join(", ", sample.MaskedColumns)}");
            }

            if (sample.Note is not null)
            {
                _out.WriteLine($"    Note: {sample.Note}");
            }

            foreach (var row in sample.Rows)
            {
                _out.WriteLine($"    {string.Join(" | ", row.Select(pair => $"{pair.Key}={pair.Value ?? "NULL"}"))}");
            }
        }

        _out.WriteLine();
    }

    private void PrintErrors(IReadOnlyList<InspectionError> errors)
    {
        if (errors.Count == 0)
        {
            return;
        }

        Section($"WARNINGS / ERRORS ({errors.Count})");
        foreach (var error in errors.Take(MaxErrorsShown))
        {
            _out.WriteLine($"  [{error.Severity}] {error.Stage} {error.Target}: {error.Message}");
        }

        if (errors.Count > MaxErrorsShown)
        {
            _out.WriteLine($"  ... {errors.Count - MaxErrorsShown} more in the JSON report.");
        }

        _out.WriteLine();
    }

    private void PrintSummary(InspectionResult result, string? reportPath, string logPath)
    {
        Section("SUMMARY");
        var instances = result.SqlInstances;
        var connected = instances.Where(instance => instance.Connection?.Success == true).ToList();

        _out.WriteLine(instances.Count > 0
            ? $"{Ok} SQL Server detected: {string.Join(", ", instances.Select(instance => instance.ServerName))}"
            : $"{Fail} No SQL Server detected");
        _out.WriteLine(connected.Count > 0
            ? $"{Ok} Connection successful: {string.Join(", ", connected.Select(instance => instance.ServerName))}"
            : $"{Fail} No connection could be established");
        _out.WriteLine($"{(result.Databases.Count > 0 ? Ok : Fail)} Databases detected: {result.Databases.Count}");

        var best = result.GameClassCandidates.FirstOrDefault(candidate => candidate.Confidence > CandidateConfidence.None);
        if (best is not null)
        {
            _out.WriteLine();
            _out.WriteLine("Potential GameClass database:");
            _out.WriteLine($"  {best.Database} on {best.Server} — confidence {best.Confidence.ToString().ToUpperInvariant()}");
            _out.WriteLine($"  Tables: {best.TableCount}");
            _out.WriteLine($"  Views: {best.ViewCount}");
        }
        else
        {
            _out.WriteLine($"{Fail} No potential GameClass database identified");
        }

        _out.WriteLine();
        _out.WriteLine("Inspection completed.");
        _out.WriteLine();
        _out.WriteLine(reportPath is null ? "Report: NOT WRITTEN (see errors above)" : $"Report: {reportPath}");
        _out.WriteLine($"Log:    {logPath}");
        _out.WriteLine();
    }

    private void Section(string title)
    {
        _out.WriteLine(title);
        _out.WriteLine(new string('-', title.Length));
    }

    private static string FormatElevation(bool? isElevated) => isElevated switch
    {
        true => " (elevated)",
        false => " (not elevated)",
        null => string.Empty,
    };

    private static string Parenthesize(string? text) => text is null ? string.Empty : $"[{text}] ";
}
