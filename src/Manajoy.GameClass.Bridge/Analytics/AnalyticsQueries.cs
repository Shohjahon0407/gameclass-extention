using System.Globalization;

namespace Manajoy.GameClass.Bridge.Analytics;

/// <summary>
/// History SELECTs for the owner's reports. Same rules as <see cref="GameClass.GameClassQueries"/>: single
/// SELECTs (ReadOnlyQueryGuard), SQL Server 2000 syntax, NOLOCK. Customers stay anonymous: only "has an
/// account" is read, never names, phones or the free-text description. Staff names come from Users.
///
/// The window bounds are our own DateTime values written as 'yyyyMMdd HH:mm:ss' literals (language-neutral on
/// every SQL Server version, identical for the ODBC and SqlClient providers); nothing user-typed reaches SQL.
/// </summary>
internal static class AnalyticsQueries
{
    public static string Literal(DateTime value) =>
        "'" + value.ToString("yyyyMMdd HH:mm:ss", CultureInfo.InvariantCulture) + "'";

    /// <summary>Segments that overlap the window, plus future reservations (status 0) that start in it.</summary>
    public static string Segments(DateTime from, DateTime to) => $"""
        SELECT S.id AS SessionId, SA.id AS SegmentId, SA.idComp AS ComputerId, SA.idTarif AS TariffId,
               SA.Start AS SegStart, SA.Stop AS SegStop, SA.TimeCost AS TimeCost, SA.TrafficCost AS TrafficCost,
               S.status AS Status, S.postpay AS Postpay, S.toResolve AS ToResolve, S.CommonPay AS CommonPay,
               S.ServiceCost AS ServiceCost, S.PrintCost AS PrintCost, S.operator AS OperatorId,
               CASE WHEN S.idClients = 0 THEN 0 ELSE 1 END AS IsAccount,
               (SELECT MIN(X.moment) FROM SessionsAdd2 AS X WITH (NOLOCK) WHERE X.idSessionsAdd = SA.id) AS FirstMoney
        FROM Sessions AS S WITH (NOLOCK)
        INNER JOIN SessionsAdd AS SA WITH (NOLOCK) ON SA.idSessions = S.id
        WHERE SA.Start < {Literal(to)} AND SA.Stop > {Literal(from)}
        ORDER BY SA.Start
        """;

    public static string MoneyEvents(DateTime from, DateTime to) => $"""
        SELECT SA.idSessions AS SessionId, SA2.moment AS Moment, SA2.summa AS Summa, SA2.ActionType AS ActionType,
               S.postpay AS Postpay, CASE WHEN S.idClients = 0 THEN 0 ELSE 1 END AS IsAccount
        FROM SessionsAdd2 AS SA2 WITH (NOLOCK)
        INNER JOIN SessionsAdd AS SA WITH (NOLOCK) ON SA.id = SA2.idSessionsAdd
        INNER JOIN Sessions AS S WITH (NOLOCK) ON S.id = SA.idSessions
        WHERE SA2.moment >= {Literal(from)} AND SA2.moment < {Literal(to)}
        """;

    public static string BarSales(DateTime from, DateTime to) => $"""
        SELECT S.idServicesBase AS ItemId, B.name AS ItemName, S.[count] AS Quantity, S.Cost AS UnitPrice,
               S.TypeCost AS TypeCost, S.moment AS Moment, S.idComputer AS ComputerId,
               CASE WHEN S.idAccount IS NULL OR S.idAccount = 0 THEN 0 ELSE 1 END AS IsAccount, S.operator AS OperatorId
        FROM Services AS S WITH (NOLOCK)
        LEFT OUTER JOIN ServicesBase AS B WITH (NOLOCK) ON B.id = S.idServicesBase
        WHERE S.moment >= {Literal(from)} AND S.moment < {Literal(to)}
        """;

    public static string AccountMoney(DateTime from, DateTime to) => $"""
        SELECT AH.what AS What, AH.summa AS Summa, AH.moment AS Moment, AH.operator AS OperatorId
        FROM AccountsHistory AS AH WITH (NOLOCK)
        WHERE AH.moment >= {Literal(from)} AND AH.moment < {Literal(to)}
        """;

    public static string Shifts(DateTime from, DateTime to) => $"""
        SELECT J.moment AS Moment, J.summa AS Summa, J.removedMoney AS Removed, J.operator AS OperatorId
        FROM JournalOp AS J WITH (NOLOCK)
        WHERE J.moment >= {Literal(from)} AND J.moment < {Literal(to)}
        ORDER BY J.moment
        """;

    /// <summary>GameClass's "Clear statistics" deletes old rows: the first day that still has data.</summary>
    public const string HistoryFrom = """
        SELECT MIN(SA.Start) AS FirstAt FROM SessionsAdd AS SA WITH (NOLOCK) WHERE SA.Start > '20000101'
        """;

    // Deleted computers stay in the list: old sessions may point at them.
    public const string AllComputers = """
        SELECT C.id AS Id, C.number AS Number, C.isdelete AS IsDeleted, G.name AS Zone
        FROM Computers AS C WITH (NOLOCK)
        LEFT OUTER JOIN ComputerGroups AS G WITH (NOLOCK) ON G.id = C.idGroup
        WHERE C.number > 0
        """;

    /// <summary>The bar price list (GameClass «Услуги»). It has no stock column: stock lives in the bridge (Bar/).</summary>
    public const string BarCatalog = """
        SELECT B.id AS Id, B.name AS Name, B.price AS Price, B.isdelete AS IsDeleted
        FROM ServicesBase AS B WITH (NOLOCK)
        """;

    /// <summary>How many of each item were sold from <paramref name="from"/> on (only what the stock book needs).</summary>
    public static string BarSalesSince(DateTime from) => $"""
        SELECT S.idServicesBase AS ItemId, S.moment AS Moment, S.[count] AS Quantity
        FROM Services AS S WITH (NOLOCK)
        WHERE S.moment >= {Literal(from)} AND S.idServicesBase IS NOT NULL
        """;

    public const string AllTariffs = "SELECT T.id AS Id, T.name AS Name FROM Tarifs AS T WITH (NOLOCK)";

    public const string Staff = "SELECT U.id AS Id, U.Name AS Name FROM Users AS U WITH (NOLOCK)";
}
