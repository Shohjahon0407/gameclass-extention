using Manajoy.GameClass.Inspector.Discovery;
using Manajoy.GameClass.Inspector.Infrastructure;
using Manajoy.GameClass.Inspector.Models;
using Microsoft.Extensions.Logging;

namespace Manajoy.GameClass.Inspector.Inspection;

/// <summary>
/// Collects structure (tables, views, columns, keys, routine names) of one database.
/// Each part is read independently so one permission problem does not hide the rest.
/// </summary>
internal sealed class SchemaInspector(
    ColumnInspector columnInspector,
    TableInspector tableInspector,
    ViewInspector viewInspector,
    RoutineInspector routineInspector,
    InspectionErrors errors,
    ILogger<SchemaInspector> logger)
{
    public async Task<DatabaseSchema> InspectAsync(ConnectedInstance instance, DatabaseInfo database, CancellationToken cancellationToken)
    {
        var target = instance.Target;
        var name = database.DatabaseName;
        var scope = $"{instance.ServerName}/{name}";
        logger.LogInformation("Inspecting schema of {Scope}", scope);

        var columns = await RunStepAsync(scope, "columns", () => columnInspector.GetColumnsAsync(target, name, cancellationToken), ColumnLookup.Empty, cancellationToken);
        var tables = await RunStepAsync(scope, "tables", () => tableInspector.GetTablesAsync(target, name, columns, cancellationToken), [], cancellationToken);
        var views = await RunStepAsync(scope, "views", () => viewInspector.GetViewsAsync(target, name, columns, cancellationToken), [], cancellationToken);
        var routines = await RunStepAsync(scope, "routines", () => routineInspector.GetRoutinesAsync(target, name, cancellationToken), [], cancellationToken);

        logger.LogInformation(
            "{Scope}: {Tables} table(s), {Views} view(s), {Routines} routine(s)",
            scope, tables.Count, views.Count, routines.Count);

        return new DatabaseSchema
        {
            Server = instance.ServerName,
            Database = name,
            Tables = tables,
            Views = views,
            Routines = routines,
        };
    }

    private async Task<T> RunStepAsync<T>(string scope, string step, Func<Task<T>> action, T fallback, CancellationToken cancellationToken)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Cannot read {Step} of {Scope}: {Message}", step, scope, ex.Message);
            errors.Warning(InspectionStage.SchemaInspection, scope, $"Cannot read {step}: {ex.Message}");
            return fallback;
        }
    }
}
