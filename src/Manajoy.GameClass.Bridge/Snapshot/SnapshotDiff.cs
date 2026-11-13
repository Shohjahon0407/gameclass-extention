namespace Manajoy.GameClass.Bridge.Snapshot;

/// <summary>Detects per-PC transitions between two snapshots.</summary>
internal static class SnapshotDiff
{
    /// <summary>The first snapshot after start-up is a baseline, not a change, so it yields no events.</summary>
    public static IReadOnlyList<StatusChangeEvent> Changes(ClubSnapshot? previous, ClubSnapshot current, Func<long> nextSequence)
    {
        if (previous is null)
        {
            return [];
        }

        var before = previous.Pcs.ToDictionary(pc => pc.ExternalId);
        var events = new List<StatusChangeEvent>();

        foreach (var pc in current.Pcs)
        {
            before.TryGetValue(pc.ExternalId, out var old);
            // State alone can change too, e.g. every PC becomes "unknown" when GCServer stops.
            var sameState = old is not null && old.GameClassState == pc.GameClassState && old.State == pc.State;
            var sameSession = old?.Session?.SessionId == pc.Session?.SessionId;
            if (sameState && sameSession)
            {
                continue;
            }

            events.Add(new StatusChangeEvent(
                nextSequence(),
                current.FetchedAt,
                pc.Number,
                pc.ExternalId,
                old?.State,
                pc.State,
                old?.GameClassState,
                pc.GameClassState,
                pc.Session?.SessionId ?? old?.Session?.SessionId));
        }

        return events;
    }
}
