using System.Text;
using System.Text.Json;
using Manajoy.GameClass.Bridge.Configuration;
using Manajoy.GameClass.Bridge.Data;
using Manajoy.GameClass.Bridge.GameClass;
using Manajoy.GameClass.Bridge.Snapshot;
using Microsoft.Extensions.Options;

namespace Manajoy.GameClass.Bridge.Hosting;

/// <summary>Polls GameClass, publishes snapshots to the store and mirrors them to disk.</summary>
internal sealed class PollingWorker(
    GameClassReader reader,
    GameClassConnectionFactory connections,
    SnapshotStore store,
    IOptions<BridgeOptions> options,
    TimeProvider time,
    ILogger<PollingWorker> logger) : BackgroundService
{
    private const int RetainDays = 30;
    private const int MoveAttempts = 5;
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly BridgeOptions _options = options.Value;
    // Events not yet appended to disk (e.g. a reader held the file); retried on the next poll.
    private readonly List<StatusChangeEvent> _pendingEvents = [];
    private bool _failing;
    private bool _fileWriteFailing;
    private DateOnly _lastCleanup;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Polling {Server}/{Database} every {Interval}s (read-only)",
            _options.Server, _options.Database, _options.PollIntervalSeconds);

        // Let the host finish starting (Kestrel included) before the first, possibly slow, ODBC poll.
        await Task.Yield();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollIntervalSeconds), time);
        do
        {
            await PollOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task PollOnceAsync(CancellationToken cancellationToken, bool writeFiles = true)
    {
        ClubSnapshot snapshot;
        IReadOnlyList<StatusChangeEvent> changes;
        try
        {
            var data = await reader.ReadAsync(cancellationToken);
            snapshot = SnapshotBuilder.Build(data, time.GetLocalNow(), BridgeInfo.Version);
            changes = store.Publish(snapshot, connections.ActiveProviderName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            store.RecordFailure(time.GetLocalNow(), ex.Message);
            connections.Reset();
            if (!_failing)
            {
                _failing = true;
                logger.LogWarning("GameClass poll failed: {Message}", ex.Message);
            }
            else
            {
                logger.LogDebug(ex, "GameClass poll failed again");
            }

            return;
        }

        if (_failing)
        {
            _failing = false;
            logger.LogInformation("GameClass database reachable again");
        }

        foreach (var change in changes)
        {
            logger.LogInformation("PC {Pc}: {From}/{FromGc} -> {To}/{ToGc} (session {Session})",
                change.PcNumber, change.FromState, change.FromGameClassState, change.ToState, change.ToGameClassState, change.SessionId);
        }

        if (writeFiles)
        {
            _pendingEvents.AddRange(changes);
            await WriteFilesAsync(snapshot, cancellationToken);
        }
    }

    private async Task WriteFilesAsync(ClubSnapshot snapshot, CancellationToken cancellationToken)
    {
        try
        {
            await WriteSnapshotFileAsync(snapshot, cancellationToken);
            await AppendPendingEventsAsync(snapshot.FetchedAt, cancellationToken);
            CleanupOldFiles(snapshot.FetchedAt);
            if (_fileWriteFailing)
            {
                _fileWriteFailing = false;
                logger.LogInformation("Snapshot/event files are being written again");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!_fileWriteFailing)
            {
                _fileWriteFailing = true;
                logger.LogWarning("Could not write snapshot/event files (will retry): {Message}", ex.Message);
            }
        }
    }

    private async Task WriteSnapshotFileAsync(ClubSnapshot snapshot, CancellationToken cancellationToken)
    {
        var path = BridgePaths.Resolve(_options.SnapshotFile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(snapshot, BridgeJson.Indented), Utf8NoBom, cancellationToken);

        // On Windows the replace fails while another process holds the target without FILE_SHARE_DELETE.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < MoveAttempts)
            {
                await Task.Delay(100 * attempt, cancellationToken);
            }
        }
    }

    private async Task AppendPendingEventsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (_pendingEvents.Count == 0)
        {
            return;
        }

        var path = DailyEventFile(now);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var lines = _pendingEvents.Select(c => JsonSerializer.Serialize(c, BridgeJson.Compact)).ToList();
        await File.AppendAllLinesAsync(path, lines, Utf8NoBom, cancellationToken);
        _pendingEvents.Clear();
    }

    /// <summary>events.jsonl becomes events-YYYYMMDD.jsonl so the files can be rotated.</summary>
    private string DailyEventFile(DateTimeOffset now)
    {
        var configured = BridgePaths.Resolve(_options.EventLogFile);
        var name = $"{Path.GetFileNameWithoutExtension(configured)}-{now:yyyyMMdd}{Path.GetExtension(configured)}";
        return Path.Combine(Path.GetDirectoryName(configured)!, name);
    }

    private void CleanupOldFiles(DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.DateTime);
        if (_lastCleanup == today)
        {
            return;
        }

        _lastCleanup = today;
        var cutoff = now.DateTime.AddDays(-RetainDays);
        var eventsFile = BridgePaths.Resolve(_options.EventLogFile);
        DeleteOlderThan(Path.GetDirectoryName(eventsFile)!, $"{Path.GetFileNameWithoutExtension(eventsFile)}-*{Path.GetExtension(eventsFile)}", cutoff);
        DeleteOlderThan(BridgePaths.Resolve(_options.LogDirectory), "bridge-*.log", cutoff);
    }

    private static void DeleteOlderThan(string directory, string pattern, DateTime cutoff)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, pattern))
        {
            if (File.GetLastWriteTime(file) < cutoff)
            {
                File.Delete(file);
            }
        }
    }
}
