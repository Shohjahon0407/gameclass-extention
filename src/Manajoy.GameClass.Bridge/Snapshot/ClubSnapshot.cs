using System.Text.Json.Serialization;

namespace Manajoy.GameClass.Bridge.Snapshot;

// Wire model. Field names and the `state` vocabulary deliberately mirror the backend's iCafe
// normalizer (available | busy | offline | disabled | unknown) so a GameClass connector can reuse it.

public static class PcStates
{
    public const string Available = "available";
    public const string Busy = "busy";
    public const string Offline = "offline";
    public const string Disabled = "disabled";
    public const string Unknown = "unknown";

    public static readonly string[] All = [Available, Busy, Offline, Disabled, Unknown];
}

/// <summary>GameClass-specific detail behind <see cref="PcStates"/>.</summary>
public static class GameClassPcStates
{
    public const string Free = "free";
    public const string InSession = "in_session";
    public const string Reserved = "reserved";
    /// <summary>No session and GCServer has lost the link to the client (client off, crashed or unplugged).</summary>
    public const string Offline = "offline";

    /// <summary>REMONT session (tariff 0): the operator took the PC out of service.</summary>
    public const string Maintenance = "maintenance";
}

public sealed record ClubSnapshot(
    [property: JsonPropertyName("v")] int Version,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("bridge_version")] string BridgeVersion,
    [property: JsonPropertyName("fetched_at")] DateTimeOffset FetchedAt,
    [property: JsonPropertyName("server_time")] DateTimeOffset ServerTime,
    [property: JsonPropertyName("club")] ClubInfo Club,
    [property: JsonPropertyName("gameclass")] GameClassServerInfo GameClass,
    [property: JsonPropertyName("counts")] IReadOnlyDictionary<string, int> Counts,
    [property: JsonPropertyName("zones")] IReadOnlyDictionary<string, ZoneSummary> Zones,
    [property: JsonPropertyName("pcs")] IReadOnlyList<PcStatus> Pcs,
    [property: JsonPropertyName("tariffs")] IReadOnlyList<TariffInfo> Tariffs,
    [property: JsonPropertyName("groups")] IReadOnlyList<GroupInfo> Groups);

/// <summary>
/// GCServer writes a heartbeat about every 5 s while an operator is logged in. When it is older than
/// 5 minutes or missing, every PC's `state` is reported as `unknown` (gc_state still shows what the DB says).
/// </summary>
public sealed record GameClassServerInfo(
    // true: heartbeat fresh; false: heartbeat stale; null: no heartbeat row exists (e.g. after GameClass
    // "Clear statistics", until the next operator logon). Per-PC state is trusted only when true.
    [property: JsonPropertyName("server_alive")] bool? ServerAlive,
    [property: JsonPropertyName("heartbeat_at")] DateTimeOffset? HeartbeatAt,
    [property: JsonPropertyName("heartbeat_age_seconds")] int? HeartbeatAgeSeconds,
    [property: JsonPropertyName("heartbeat_missing")] bool HeartbeatMissing,
    [property: JsonPropertyName("heartbeat_disabled")] bool HeartbeatDisabled,
    [property: JsonPropertyName("utc_offset_minutes")] int UtcOffsetMinutes);

/// <summary>Club-wide money limits from the GameClass Registry (null = not set: GameClass uses 5 / 200 / 200).</summary>
public sealed record ClubInfo(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("currency")] string? Currency,
    [property: JsonPropertyName("start_money_minimum")] decimal? StartMoneyMinimum = null,
    [property: JsonPropertyName("start_money_maximum")] decimal? StartMoneyMaximum = null,
    [property: JsonPropertyName("max_trust_postpay")] decimal? MaxTrustPostpay = null);

public sealed record ZoneSummary(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("available")] int Available,
    [property: JsonPropertyName("busy")] int Busy,
    [property: JsonPropertyName("offline")] int Offline,
    [property: JsonPropertyName("unknown")] int Unknown,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("next_free_in")] int? NextFreeInMinutes);

public sealed record PcStatus(
    [property: JsonPropertyName("external_id")] int ExternalId,
    [property: JsonPropertyName("external_name")] string ExternalName,
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("ip")] string Ip,
    [property: JsonPropertyName("group_id")] int GroupId,
    [property: JsonPropertyName("area_name")] string AreaName,
    [property: JsonPropertyName("vip")] bool Vip,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("gc_state")] string GameClassState,
    [property: JsonPropertyName("link_lost")] bool LinkLost,
    [property: JsonPropertyName("time_left_minutes")] int? TimeLeftMinutes,
    [property: JsonPropertyName("session")] SessionInfo? Session,
    [property: JsonPropertyName("upcoming_reservations")] IReadOnlyList<ReservationInfo> UpcomingReservations,
    [property: JsonPropertyName("icafe_compat")] ICafeCompatPc ICafeCompat);

/// <summary>
/// The same PC expressed in iCafeCloud /pcs field names, so the backend's existing
/// icafe normalizer (normalize_pc / build_envelope) can consume GameClass data unchanged.
/// </summary>
public sealed record ICafeCompatPc(
    [property: JsonPropertyName("pc_name")] string PcName,
    [property: JsonPropertyName("pc_area_name")] string PcAreaName,
    [property: JsonPropertyName("pc_group_id")] int PcGroupId,
    [property: JsonPropertyName("pc_console_type")] int PcConsoleType,
    // null for unknown states: the backend normalizer maps missing values to "unknown", never guessing.
    [property: JsonPropertyName("pc_enabled")] int? PcEnabled,
    [property: JsonPropertyName("pc_in_using")] int? PcInUsing,
    [property: JsonPropertyName("pc_box_left")] int? PcBoxLeft,
    [property: JsonPropertyName("pc_box_top")] int? PcBoxTop,
    [property: JsonPropertyName("status_connect_time_left")] string? StatusConnectTimeLeft);

public sealed record SessionInfo(
    [property: JsonPropertyName("session_id")] int SessionId,
    [property: JsonPropertyName("segment_id")] int SegmentId,
    [property: JsonPropertyName("tariff_id")] int TariffId,
    [property: JsonPropertyName("tariff_name")] string? TariffName,
    [property: JsonPropertyName("started_at")] DateTimeOffset StartedAt,
    [property: JsonPropertyName("segment_started_at")] DateTimeOffset SegmentStartedAt,
    [property: JsonPropertyName("ends_at")] DateTimeOffset? EndsAt,
    [property: JsonPropertyName("postpay")] bool Postpay,
    // Sessions.CommonPay means the prepaid amount for prepaid sessions and the credit limit for postpay ones.
    [property: JsonPropertyName("prepaid_amount")] decimal? PrepaidAmount,
    [property: JsonPropertyName("postpay_limit")] decimal? PostpayLimit,
    // Spent so far over the whole session (all segments), the same scope as prepaid_amount. Refreshed by
    // GCServer in rounding steps, so it can lag a few seconds.
    [property: JsonPropertyName("time_cost")] decimal TimeCost,
    // Packet tariffs: GameClass rewrites the start times to the packet window, and time_cost is the whole packet
    // price from the first minute, so use ends_at / time_left rather than money for these.
    [property: JsonPropertyName("is_packet")] bool IsPacket,
    [property: JsonPropertyName("has_account")] bool HasAccount,
    [property: JsonPropertyName("gc_state")] int GameClassSessionState,
    [property: JsonPropertyName("gc_status")] int GameClassSessionStatus,
    // For the cashier's "add money" estimate (GameClass RecalcStopTime): what the current segment cost so far,
    // penalty minutes and extra charges that are taken out of the prepaid amount.
    [property: JsonPropertyName("segment_time_cost")] decimal? SegmentTimeCost = null,
    [property: JsonPropertyName("additional_minutes")] int? AdditionalMinutes = null,
    [property: JsonPropertyName("extra_cost")] decimal? ExtraCost = null);

public sealed record ReservationInfo(
    [property: JsonPropertyName("session_id")] int SessionId,
    [property: JsonPropertyName("starts_at")] DateTimeOffset StartsAt,
    [property: JsonPropertyName("ends_at")] DateTimeOffset EndsAt,
    [property: JsonPropertyName("tariff_id")] int TariffId);

public sealed record TariffInfo(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("internet")] bool Internet,
    [property: JsonPropertyName("group_id")] int GroupId,
    [property: JsonPropertyName("round_money")] decimal RoundMoney,
    [property: JsonPropertyName("variants")] IReadOnlyList<TariffVariantInfo> Variants,
    [property: JsonPropertyName("round_time_seconds")] int? RoundTimeSeconds = null,
    [property: JsonPropertyName("use_separate_sum")] bool? UseSeparateSum = null,
    [property: JsonPropertyName("start_money_min")] decimal? StartMoneyMin = null,
    [property: JsonPropertyName("start_money_max")] decimal? StartMoneyMax = null,
    [property: JsonPropertyName("maximum_trust")] decimal? MaximumTrust = null);

public sealed record TariffVariantInfo(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("from")] string From,
    [property: JsonPropertyName("to")] string To,
    [property: JsonPropertyName("cost")] decimal Cost,
    [property: JsonPropertyName("is_packet")] bool IsPacket,
    [property: JsonPropertyName("days_of_week")] string DaysOfWeek,
    // Exact times (from/to drop the seconds) and the fields GameClass's pricing needs.
    [property: JsonPropertyName("from_seconds")] int? FromSeconds = null,
    [property: JsonPropertyName("to_seconds")] int? ToSeconds = null,
    [property: JsonPropertyName("condition")] string? Condition = null,
    [property: JsonPropertyName("free_packet")] bool? FreePacket = null);

public sealed record GroupInfo(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name);

/// <summary>Emitted when a PC changes state or session.</summary>
public sealed record StatusChangeEvent(
    [property: JsonPropertyName("seq")] long Sequence,
    [property: JsonPropertyName("at")] DateTimeOffset At,
    [property: JsonPropertyName("pc_number")] int PcNumber,
    [property: JsonPropertyName("external_id")] int ExternalId,
    [property: JsonPropertyName("from_state")] string? FromState,
    [property: JsonPropertyName("to_state")] string ToState,
    [property: JsonPropertyName("from_gc_state")] string? FromGameClassState,
    [property: JsonPropertyName("to_gc_state")] string ToGameClassState,
    [property: JsonPropertyName("session_id")] int? SessionId);
