using System.Globalization;
using Manajoy.GameClass.Bridge.GameClass;

namespace Manajoy.GameClass.Bridge.Snapshot;

/// <summary>
/// Pure transformation of one polling round into the wire snapshot. No I/O, so it is unit-tested directly.
/// </summary>
internal static class SnapshotBuilder
{
    public const int EnvelopeVersion = 1;
    public const string ProviderName = "gameclass";

    /// <summary>Sessions.status (TGCSessionStatus): 0 reserved (not yet activated), 1 active, 2 finished.</summary>
    public const int ReservedSessionStatus = 0;

    public const int ActiveSessionStatus = 1;

    /// <summary>SessionsAdd.idTarif 0 is GameClass's REMONT (maintenance) pseudo-tariff.</summary>
    public const int MaintenanceTariffId = 0;

    /// <summary>
    /// GCServer rewrites the club heartbeat about every 5 s while an operator is logged in. A modal dialog can
    /// pause it, so only a heartbeat older than this means GCServer is down (or in manager-only mode).
    /// </summary>
    public static readonly TimeSpan HeartbeatStaleAfter = TimeSpan.FromSeconds(300);

    /// <summary>GCServer's own timer that writes the club heartbeat (GLOBAL_TIMER).</summary>
    private const int GlobalTimerSeconds = 5;

    /// <summary>Registry 'ClientQueryTime' default: how often GCServer pings each client.</summary>
    private const int DefaultClientQueryTimeSeconds = 5;

    public static ClubSnapshot Build(GameClassData data, DateTimeOffset fetchedAt, string bridgeVersion)
    {
        var now = data.ServerNow;
        var offset = data.ServerUtcOffset;
        var groupNames = data.Groups.ToDictionary(g => g.Id, g => g.Name);
        var tariffNames = data.Tariffs.ToDictionary(t => t.Id, t => t.Name);
        var segmentsByComputer = data.Segments.ToLookup(s => s.ComputerId);
        var heartbeat = data.LastHeartbeat;
        var heartbeatAge = heartbeat is { } h ? now - h : (TimeSpan?)null;
        // false: the heartbeat stopped. GCServer is closed or only a manager is logged in; the DB is then a frozen
        // picture (expired sessions are not stopped, lost links are not recorded).
        // null: there is no heartbeat row at all. GameClass "Clear statistics" deletes it (together with running
        // sessions' headers) and only the next operator logon recreates it, so liveness cannot be proven either.
        // In both cases per-PC state is reported as unknown: a wrong "available" is worse than "unknown".
        bool? serverAlive = heartbeatAge is { } age ? age <= HeartbeatStaleAfter : null;

        // A club can switch the heartbeat off (Registry WriteUnControlClub=0). Liveness is then unknowable, so the
        // time-based session data is trusted as is and no PC is ever reported offline.
        var heartbeatDisabled = !data.Club.HeartbeatEnabled;
        var stateTrusted = heartbeatDisabled || serverAlive == true;

        // A computer's link-lost row is open while GCServer keeps extending it. Rows are extended by the per-client
        // ping cycle (ClientQueryTime), on its own timer, so allow two cycles plus one heartbeat tick of slack.
        var queryTime = data.Club.ClientQueryTimeSeconds is int configured && configured > 0 ? configured : DefaultClientQueryTimeSeconds;
        var linkTolerance = TimeSpan.FromSeconds(Math.Max(15, (2 * queryTime) + GlobalTimerSeconds));
        var linkLostComputers = serverAlive == true && heartbeat is { } beat
            ? data.Uncontrolled
                .Where(u => !u.IsClubHeartbeat && u.Stop >= beat - linkTolerance)
                .Select(u => u.ComputerId)
                .ToHashSet()
            : [];

        var pcs = data.Computers
            .OrderBy(c => c.Number)
            .Select(c => BuildPc(c, segmentsByComputer[c.Id].ToList(), linkLostComputers.Contains(c.Id), stateTrusted, now,
                groupNames, tariffNames, offset))
            .ToList();

        var counts = PcStates.All.ToDictionary(s => s, _ => 0);
        foreach (var pc in pcs)
        {
            counts[pc.State]++;
        }

        counts["total"] = pcs.Count;

        var zones = pcs
            .GroupBy(pc => pc.GroupId)
            .OrderBy(g => g.Key)
            .ToDictionary(
                g => g.Key.ToString(CultureInfo.InvariantCulture),
                g => new ZoneSummary(
                    groupNames.GetValueOrDefault(g.Key, string.Empty),
                    g.Count(pc => pc.State == PcStates.Available),
                    g.Count(pc => pc.State == PcStates.Busy),
                    g.Count(pc => pc.State == PcStates.Offline),
                    g.Count(pc => pc.State == PcStates.Unknown),
                    g.Count(),
                    g.Where(pc => pc.State == PcStates.Busy && pc.TimeLeftMinutes is not null)
                        .Select(pc => pc.TimeLeftMinutes)
                        .Min()));

        var variantsByTariff = data.TariffVariants.ToLookup(v => v.TariffId);
        var tariffs = data.Tariffs
            .Select(t => new TariffInfo(t.Id, t.Name, t.Internet, t.GroupId, t.RoundMoney,
                variantsByTariff[t.Id].Select(v => new TariffVariantInfo(
                    v.Id, v.Name, FormatTimeOfDay(v.Start), FormatTimeOfDay(v.Stop), v.Cost, v.IsPacket, v.DaysOfWeek,
                    (int)v.Start.TimeOfDay.TotalSeconds, (int)v.Stop.TimeOfDay.TotalSeconds,
                    string.IsNullOrWhiteSpace(v.Condition) ? null : v.Condition.Trim(), v.FreePacket)).ToList(),
                t.RoundTimeSeconds, t.UseSeparateSum > 0, t.StartMoneyMin, t.StartMoneyMax, t.MaximumTrust))
            .ToList();

        return new ClubSnapshot(
            EnvelopeVersion,
            ProviderName,
            bridgeVersion,
            fetchedAt,
            ToOffset(now, offset),
            new ClubInfo(data.Club.ClubName, data.Club.Currency,
                data.Club.StartMoneyMinimum, data.Club.StartMoneyMaximum, data.Club.MaximumTrustPostPay),
            new GameClassServerInfo(
                serverAlive,
                heartbeat is { } hb ? ToOffset(hb, offset) : null,
                heartbeatAge is { } a ? (int)Math.Max(0, a.TotalSeconds) : null,
                heartbeat is null && !heartbeatDisabled,
                heartbeatDisabled,
                (int)offset.TotalMinutes),
            counts,
            zones,
            pcs,
            tariffs,
            data.Groups.Select(g => new GroupInfo(g.Id, g.Name)).ToList());
    }

    private static PcStatus BuildPc(
        ComputerRow computer,
        List<SessionSegmentRow> segments,
        bool linkLost,
        bool stateTrusted,
        DateTime now,
        IReadOnlyDictionary<int, string> groupNames,
        IReadOnlyDictionary<int, string> tariffNames,
        TimeSpan tz)
    {
        // Several segments can be current at once only in corrupt data; the most recently started one wins.
        var active = segments
            .Where(s => s.Status == ActiveSessionStatus && s.SegmentStart <= now && now < s.SegmentStop)
            .OrderByDescending(s => s.SegmentStart)
            .FirstOrDefault();

        var heldReservation = active is null
            ? segments
                .Where(s => s.Status == ReservedSessionStatus && s.SegmentStart <= now && now < s.SegmentStop)
                .OrderBy(s => s.SegmentStart)
                .FirstOrDefault()
            : null;

        var upcoming = segments
            .Where(s => s.Status == ReservedSessionStatus && s.SegmentStart > now)
            .OrderBy(s => s.SegmentStart)
            .Select(s => new ReservationInfo(s.SessionId, ToOffset(s.SegmentStart, tz), ToOffset(s.SegmentStop, tz), s.TariffId))
            .ToList();

        string gcState;
        string state;
        SessionSegmentRow? current;
        if (active is { TariffId: MaintenanceTariffId })
        {
            (gcState, state, current) = (GameClassPcStates.Maintenance, PcStates.Disabled, active);
        }
        else if (active is not null)
        {
            (gcState, state, current) = (GameClassPcStates.InSession, PcStates.Busy, active);
        }
        else if (heldReservation is not null)
        {
            (gcState, state, current) = (GameClassPcStates.Reserved, PcStates.Busy, heldReservation);
        }
        else if (linkLost)
        {
            (gcState, state, current) = (GameClassPcStates.Offline, PcStates.Offline, null);
        }
        else
        {
            (gcState, state, current) = (GameClassPcStates.Free, PcStates.Available, null);
        }

        if (!stateTrusted)
        {
            state = PcStates.Unknown;
        }

        // Time left is meaningful only for a prepaid session the customer is using right now.
        int? timeLeft = state == PcStates.Busy && current is { Postpay: false }
            ? Math.Max(0, (int)Math.Floor((current.SegmentStop - now).TotalMinutes))
            : null;

        var session = current is null
            ? null
            : new SessionInfo(
                current.SessionId,
                current.SegmentId,
                current.TariffId,
                tariffNames.GetValueOrDefault(current.TariffId),
                ToOffset(current.Started, tz),
                ToOffset(current.SegmentStart, tz),
                current.Postpay ? null : ToOffset(current.SegmentStop, tz),
                current.Postpay,
                current.Postpay ? null : current.CommonPay,
                current.Postpay ? current.CommonPay : null,
                current.SessionTimeCost,
                current.Whole,
                // REMONT rows reuse idClients for whatever account was logged in; it is not a customer session.
                current.AccountId != 0 && current.TariffId != MaintenanceTariffId,
                current.State,
                current.Status,
                current.TimeCost,
                current.AdditionalMinutes,
                current.SeparateTrafficPay + current.PrintCost + current.ServiceCost);

        var areaName = groupNames.GetValueOrDefault(computer.GroupId, string.Empty);
        var compat = new ICafeCompatPc(
            computer.Number.ToString(CultureInfo.InvariantCulture),
            areaName,
            computer.GroupId,
            PcConsoleType: 0,
            PcEnabled: state switch { PcStates.Unknown => null, PcStates.Disabled => 0, _ => 1 },
            PcInUsing: state switch { PcStates.Unknown => null, PcStates.Busy => 1, _ => 0 },
            PcBoxLeft: null,
            PcBoxTop: null,
            StatusConnectTimeLeft: timeLeft is not null ? FormatTimeLeft(current!.SegmentStop - now) : null);

        return new PcStatus(
            computer.Id,
            computer.Number.ToString(CultureInfo.InvariantCulture),
            computer.Number,
            computer.IpAddress,
            computer.GroupId,
            areaName,
            computer.IsVip,
            state,
            gcState,
            linkLost,
            timeLeft,
            session,
            upcoming,
            compat);
    }

    /// <summary>iCafe's "HH:MM:SS" (hours may exceed 24); a negative remainder is reported as zero.</summary>
    internal static string FormatTimeLeft(TimeSpan remaining)
    {
        var seconds = Math.Max(0L, (long)Math.Floor(remaining.TotalSeconds));
        return string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}");
    }

    /// <summary>GameClass datetimes are naive wall-clock times of the SQL host; attach that host's UTC offset.</summary>
    public static DateTimeOffset ToOffset(DateTime serverLocal, TimeSpan utcOffset) =>
        new(DateTime.SpecifyKind(serverLocal, DateTimeKind.Unspecified), utcOffset);

    private static string FormatTimeOfDay(DateTime value) => value.ToString("HH:mm", CultureInfo.InvariantCulture);
}
