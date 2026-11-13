using System.Collections.Concurrent;
using Manajoy.GameClass.Inspector.Models;

namespace Manajoy.GameClass.Inspector.Infrastructure;

/// <summary>Collects non-fatal problems so they end up in the final report instead of stopping the run.</summary>
internal sealed class InspectionErrors
{
    private readonly ConcurrentQueue<InspectionError> _errors = new();

    public void Warning(string stage, string? target, string message) => Add(stage, target, message, ErrorSeverity.Warning);

    public void Error(string stage, string? target, string message) => Add(stage, target, message, ErrorSeverity.Error);

    public IReadOnlyList<InspectionError> Snapshot() => _errors.ToArray();

    private void Add(string stage, string? target, string message, ErrorSeverity severity) =>
        _errors.Enqueue(new InspectionError(stage, target, message, severity, DateTimeOffset.Now));
}
