using Manajoy.GameClass.Inspector.Infrastructure;
using Manajoy.GameClass.Inspector.Models;

namespace Manajoy.GameClass.Inspector.Inspection;

internal sealed class ViewInspector(ReadOnlyQueryExecutor executor)
{
    public async Task<IReadOnlyList<ViewInfo>> GetViewsAsync(
        ConnectionTarget target,
        string database,
        ColumnLookup columns,
        CancellationToken cancellationToken)
    {
        var views = await executor.QueryAsync(
            target,
            database,
            SqlQueries.Views,
            reader => (
                Schema: reader.GetRequiredString("SchemaName"),
                Name: reader.GetRequiredString("ViewName"),
                Definition: reader.GetStringOrNull("Definition")),
            cancellationToken);

        return views
            .Select(view => new ViewInfo
            {
                Schema = view.Schema,
                Name = view.Name,
                Definition = view.Definition?.Trim(),
                Columns = columns.For(view.Schema, view.Name),
            })
            .ToList();
    }
}
