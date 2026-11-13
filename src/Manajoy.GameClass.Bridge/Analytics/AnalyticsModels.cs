namespace Manajoy.GameClass.Bridge.Analytics;

// Raw rows read from GameClass for one window (Analytics/AnalyticsQueries.cs). Times are GameClass's own
// naive local times (the SQL host's clock), exactly as stored. No customer names, phones or free text.

/// <summary>One part of a session on one computer (SessionsAdd + its Sessions row).</summary>
public sealed record SegmentRow(
    int SessionId,
    int SegmentId,
    int ComputerId,
    int TariffId,
    DateTime Start,
    DateTime Stop,
    decimal TimeCost,
    decimal TrafficCost,
    int Status,
    bool Postpay,
    bool ToResolve,
    decimal CommonPay,
    decimal ServiceCost,
    decimal PrintCost,
    int OperatorId,
    bool IsAccount,
    DateTime? FirstMoney);

/// <summary>SessionsAdd2: 0 = paid at start, 1 = money added, 2 = recalculation / change, 3 and 4 = traffic.</summary>
public sealed record MoneyEventRow(int SessionId, DateTime Moment, decimal Amount, int ActionType, bool Postpay, bool IsAccount);

/// <summary>Services: TypeCost 1 = paid separately (cash), 2 = taken from a session's prepayment, 3 = added to a postpaid session.</summary>
public sealed record BarSaleRow(int ItemId, string ItemName, int Quantity, decimal UnitPrice, int TypeCost, DateTime Moment, int ComputerId, bool IsAccount, int OperatorId);

/// <summary>AccountsHistory: 0 = topped up, 1 = returned, 2 = spent.</summary>
public sealed record AccountMoneyRow(int What, decimal Amount, DateTime Moment, int OperatorId);

/// <summary>JournalOp: a closed shift.</summary>
public sealed record ShiftRow(DateTime Moment, decimal Total, decimal Removed, int OperatorId);

public sealed record AnalyticsComputer(int Id, int Number, string Zone);

/// <summary>Everything the builder needs for one request (the window plus the one before it, for comparison).</summary>
public sealed record AnalyticsData(
    DateTime Now,
    string? Currency,
    DateTime? HistoryFrom,
    IReadOnlyList<AnalyticsComputer> Computers,
    IReadOnlyDictionary<int, string> Tariffs,
    IReadOnlyDictionary<int, string> Operators,
    IReadOnlyList<SegmentRow> Segments,
    IReadOnlyList<MoneyEventRow> MoneyEvents,
    IReadOnlyList<BarSaleRow> BarSales,
    IReadOnlyList<AccountMoneyRow> AccountMoney,
    IReadOnlyList<ShiftRow> Shifts);

// ---- the report (JSON for the setup window) ----

/// <param name="From">First business day (inclusive).</param>
/// <param name="To">Last business day (inclusive).</param>
/// <param name="Group">hour | day | week | month.</param>
public sealed record AnalyticsQuery(DateOnly From, DateOnly To, string Group, int DayStartHour);

/// <summary>
/// Cash in, the way GameClass's own shift report counts it (ReportCurrent): Time + bar paid separately + print
/// + internet + account top-ups − account returns. Bar sold through a session is inside Time there; here it is
/// moved to <see cref="Bar"/>, so <see cref="PcTime"/> is time only and nothing is counted twice.
/// </summary>
public sealed record MoneySummary(
    decimal Kassa,
    decimal PcTime,
    decimal Bar,
    decimal BarCash,
    decimal BarInSessions,
    decimal BarFromAccounts,
    decimal Print,
    decimal Internet,
    decimal AccountsAdded,
    decimal AccountsReturned);

public sealed record UsageSummary(double BusyHours, double Occupancy, int PcCount, int PeakBusy, DateTime? PeakAt);

public sealed record SessionSummary(int Count, double AvgMinutes, decimal AvgCheck, int WithAccount, int Debts);

public sealed record BookingSummary(int Upcoming, int Past, int Cancelled);

public sealed record KpiCompare(decimal Kassa, decimal PcTime, decimal Bar, double BusyHours, double Occupancy, int Sessions);

public sealed record PeriodPoint(string Key, DateTime Start, decimal Kassa, decimal PcTime, decimal Bar, double BusyHours, int Sessions);

public sealed record PcStat(int Number, string Zone, double BusyHours, double Occupancy, int Sessions, decimal TimeValue, DateTime? LastUsed);

public sealed record ZoneStat(string Zone, int Pcs, double BusyHours, double Occupancy, decimal TimeValue);

public sealed record TariffStat(string Name, int Sessions, double Hours, decimal TimeValue);

public sealed record BarItemStat(string Name, int Quantity, decimal Total, decimal Cash, decimal InSessions, decimal FromAccounts);

public sealed record OperatorStat(string Name, int Sessions, decimal TimeValue, decimal Bar);

public sealed record ShiftStat(DateTime At, string Operator, decimal Total, decimal Removed);

public sealed record DebtStat(int Pc, DateTime At, decimal SessionTotal);

public sealed record AnalyticsReport(
    string From,
    string To,
    string Group,
    int DayStartHour,
    DateTime WindowStart,
    DateTime WindowEnd,
    DateTime GeneratedAt,
    string? Currency,
    DateTime? HistoryFrom,
    MoneySummary Money,
    UsageSummary Usage,
    SessionSummary Sessions,
    BookingSummary Bookings,
    KpiCompare Previous,
    IReadOnlyList<PeriodPoint> Series,
    IReadOnlyList<PcStat> Pcs,
    IReadOnlyList<ZoneStat> Zones,
    IReadOnlyList<TariffStat> Tariffs,
    IReadOnlyList<BarItemStat> Bar,
    IReadOnlyList<OperatorStat> Operators,
    IReadOnlyList<ShiftStat> Shifts,
    IReadOnlyList<DebtStat> Debts,
    IReadOnlyList<double[]> Heatmap);

public sealed record TimelineBlock(DateTime Start, DateTime End, string Tariff, decimal TimeValue, bool Booked, bool Active, bool Account, bool Reserved);

public sealed record TimelinePc(int Number, string Zone, double BusyHours, IReadOnlyList<TimelineBlock> Blocks);

public sealed record AnalyticsDay(string Date, DateTime WindowStart, DateTime WindowEnd, DateTime Now, IReadOnlyList<TimelinePc> Pcs);

/// <summary>Where GameClass data for a window comes from (the real database, or the demo generator).</summary>
public interface IAnalyticsSource
{
    Task<AnalyticsData> LoadAsync(DateTime from, DateTime to, CancellationToken cancellationToken);
}

// ---- one business day, sent to Manajoy (snake_case JSON). Every number adds up across days, so the backend can
// build any period from days: occupancy = Σ busy / Σ capacity, average check = Σ paid_sum / Σ paid_count.

public sealed record DayRollup(
    string Date,
    bool Complete,
    MoneySummary Money,
    RollupUsage Usage,
    IReadOnlyList<double> HourlyBusyMinutes,
    IReadOnlyList<RollupHour> HourlyMoney,
    RollupSessions Sessions,
    RollupBookings Bookings,
    IReadOnlyList<RollupPc> Pcs,
    IReadOnlyList<RollupZone> Zones,
    IReadOnlyList<RollupTariff> Tariffs,
    IReadOnlyList<BarItemStat> Bar,
    IReadOnlyList<OperatorStat> Operators,
    IReadOnlyList<ShiftStat> Shifts,
    IReadOnlyList<DebtStat> Debts,
    IReadOnlyList<RollupBlock> Timeline,
    string Fingerprint = "");

/// <param name="CapacityMinutes">Live PCs × the minutes of the day that have passed (and that GameClass has history for).</param>
public sealed record RollupUsage(double BusyMinutes, double CapacityMinutes, int PcCount, int PeakBusy, DateTime? PeakAt);

/// <summary>Index = clock hour (0..23), like <see cref="DayRollup.HourlyBusyMinutes"/>.</summary>
public sealed record RollupHour(decimal Kassa, decimal PcTime, decimal Bar, int Sessions);

public sealed record RollupSessions(int Count, double Minutes, int PaidCount, decimal PaidSum, int WithAccount, int Debts);

public sealed record RollupBookings(int Past, int Cancelled);

public sealed record RollupPc(int Number, string Zone, double BusyMinutes, int Sessions, decimal TimeValue, DateTime? LastUsed);

public sealed record RollupZone(string Zone, int Pcs, double BusyMinutes, decimal TimeValue);

public sealed record RollupTariff(string Name, int Sessions, double BusyMinutes, decimal TimeValue);

public sealed record RollupBlock(int Pc, DateTime Start, DateTime End, string Tariff, decimal Value, bool Booked, bool Account, bool Reserved, bool Active);
