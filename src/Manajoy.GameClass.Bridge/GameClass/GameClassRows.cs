namespace Manajoy.GameClass.Bridge.GameClass;

// Raw rows exactly as GameClass stores them. Datetimes are the GameClass server's local wall-clock time.

internal sealed record ComputerRow(int Id, int Number, string IpAddress, int GroupId, bool IsVip, int ClientType, bool IgnoreOffline);

internal sealed record ComputerGroupRow(int Id, string Name);

/// <param name="RoundTimeSeconds">Tarifs.roundtime: the billing step in SECONDS (1 = per-second/minute billing).</param>
/// <param name="UseSeparateSum">When &gt; 0 the tariff's own start-money limits and trust replace the Registry ones.</param>
internal sealed record TariffRow(
    int Id,
    string Name,
    bool Internet,
    int GroupId,
    decimal RoundMoney,
    int RoundTimeSeconds,
    int Priority,
    int UseSeparateSum = 0,
    decimal StartMoneyMin = 0,
    decimal StartMoneyMax = 0,
    decimal MaximumTrust = 0);

internal sealed record TariffVariantRow(
    int Id,
    int TariffId,
    string Name,
    DateTime Start,
    DateTime Stop,
    decimal Cost,
    bool IsPacket,
    string DaysOfWeek,
    string? Condition = null,
    bool FreePacket = false);

/// <summary>One SessionsAdd segment joined with its Sessions header.</summary>
internal sealed record SessionSegmentRow(
    int SessionId,
    int SegmentId,
    int ComputerId,
    int AccountId,
    DateTime Started,
    DateTime SegmentStart,
    DateTime SegmentStop,
    int TariffId,
    bool Whole,
    bool Postpay,
    int State,
    int Status,
    decimal CommonPay,
    decimal SeparateTrafficPay,
    decimal PrintCost,
    decimal ServiceCost,
    int AdditionalMinutes,
    double Traffic,
    decimal TimeCost,
    decimal SessionTimeCost);

internal sealed record UncontrolRow(int ComputerId, DateTime Start, DateTime Stop, bool IsClubHeartbeat);

/// <param name="HeartbeatEnabled">Registry WriteUnControlClub (3.85.6); when "0" GCServer never writes its heartbeat.</param>
/// <param name="StartMoneyMinimum">Registry StartMoneyMinimum etc. (GameClass defaults 5 / 200 / 200 when absent).</param>
internal sealed record ClubSettings(
    string? ClubName,
    string? Currency,
    int? ClientQueryTimeSeconds = null,
    bool HeartbeatEnabled = true,
    decimal? StartMoneyMinimum = null,
    decimal? StartMoneyMaximum = null,
    decimal? MaximumTrustPostPay = null);

/// <summary>Everything read in one polling round.</summary>
internal sealed record GameClassData(
    DateTime ServerNow,
    TimeSpan ServerUtcOffset,
    ClubSettings Club,
    DateTime? LastHeartbeat,
    IReadOnlyList<ComputerRow> Computers,
    IReadOnlyList<ComputerGroupRow> Groups,
    IReadOnlyList<TariffRow> Tariffs,
    IReadOnlyList<TariffVariantRow> TariffVariants,
    IReadOnlyList<SessionSegmentRow> Segments,
    IReadOnlyList<UncontrolRow> Uncontrolled);
