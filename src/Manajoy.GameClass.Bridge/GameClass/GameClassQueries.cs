namespace Manajoy.GameClass.Bridge.GameClass;

/// <summary>
/// Every statement the bridge sends. All are single SELECTs (enforced by ReadOnlyQueryGuard),
/// SQL Server 2000 compatible (no CTE / ROW_NUMBER / OFFSET) and use NOLOCK so the live
/// GameClass server is never blocked. Personal data (account names, phones, passwords,
/// operator free-text descriptions) is never selected.
/// </summary>
internal static class GameClassQueries
{
    /// <summary>GameClass stores naive local times of the SQL host; its UTC offset is taken from the server itself.</summary>
    /// Seconds are rounded to minutes so two GETDATE()/GETUTCDATE() calls straddling a minute boundary cannot give 299/301.
    public const string ServerNow = "SELECT GETDATE() AS ServerNow, CAST(ROUND(DATEDIFF(second, GETUTCDATE(), GETDATE()) / 60.0, 0) AS int) AS UtcOffsetMinutes";

    public const string ClubSettings = """
        SELECT [key] AS SettingKey, CAST([value] AS nvarchar(200)) AS SettingValue
        FROM Registry WITH (NOLOCK)
        WHERE [key] IN ('ClubName', 'Currency', 'ClientQueryTime', 'WriteUnControlClub',
                        'StartMoneyMinimum', 'StartMoneyMaximum', 'MaximumTrustPostPay')
        """;

    // Number 0 is GameClass's hidden "Club" pseudo-computer; deleted computers are kept for history.
    public const string Computers = """
        SELECT id, number, ipaddress, idGroup, vip, client_type, ignore_offline
        FROM Computers WITH (NOLOCK)
        WHERE isdelete = 0 AND number > 0
        ORDER BY number
        """;

    public const string ComputerGroups = """
        SELECT id, name
        FROM ComputerGroups WITH (NOLOCK)
        WHERE isdelete = 0
        ORDER BY id
        """;

    public const string Tariffs = """
        SELECT id, name, internet, idGroup, roundmoney, roundtime, priorityshow,
               useseparatesumm, startmoneymin, startmoneymax, maximumtrust
        FROM Tarifs WITH (NOLOCK)
        WHERE isdelete = 0
        ORDER BY priorityshow, id
        """;

    public const string TariffVariants = """
        SELECT V.id, V.idTarifs, V.name, V.start, V.stop, V.cost, V.ispacket, V.daysofweek,
               V.condition, V.FreePacket
        FROM TarifsVariants AS V WITH (NOLOCK)
        INNER JOIN Tarifs AS T WITH (NOLOCK) ON T.id = V.idTarifs
        WHERE T.isdelete = 0
        ORDER BY V.idTarifs, V.id
        """;

    /// <summary>
    /// Same selection rule as GameClass's own SessionsSelect procedure: a segment counts when it has
    /// started (or is a not-yet-activated reservation, status 0) and has not reached its stop time.
    /// Only status 0 (reserved) and 1 (active) are relevant; SessionsStop sets status 2 and moves [stop] to the
    /// actual end. SessionTimeCost is the whole session's spend (all segments), matching Sessions.CommonPay.
    /// </summary>
    public const string CurrentSegments = """
        SELECT S.id AS SessionId, SA.id AS SegmentId, SA.idComp AS ComputerId, S.idClients AS AccountId,
               S.started AS Started, SA.Start AS SegmentStart, SA.Stop AS SegmentStop, SA.idTarif AS TariffId,
               SA.whole AS Whole, S.postpay AS Postpay, S.state AS SessionState, S.status AS SessionStatus,
               S.CommonPay AS CommonPay, S.SeparateTrafficPay AS SeparateTrafficPay, S.PrintCost AS PrintCost,
               S.ServiceCost AS ServiceCost, S.AdditionalMinutes AS AdditionalMinutes,
               SA.Traffic AS Traffic, SA.TimeCost AS TimeCost,
               (SELECT SUM(X.TimeCost) FROM SessionsAdd AS X WITH (NOLOCK) WHERE X.idSessions = S.id) AS SessionTimeCost
        FROM Sessions AS S WITH (NOLOCK)
        INNER JOIN SessionsAdd AS SA WITH (NOLOCK) ON SA.idSessions = S.id
        WHERE S.status IN (0, 1) AND (SA.Start <= GETDATE() OR S.status = 0) AND GETDATE() < SA.Stop
        ORDER BY SA.idComp, SA.Start
        """;

    /// <summary>
    /// "Uncontrol" intervals that GCServer is still extending. GCServer opens a row when it loses the link
    /// to a computer's client (UnControlCompStart) and pushes [stop] forward on every pulse (UnControlCompTimer).
    /// The hidden 'Club' row is GCServer's own heartbeat (UnControlClubTimer, about every 5 s in operator mode).
    /// The 300 s window matches SnapshotBuilder.HeartbeatStaleAfter.
    /// </summary>
    public const string RecentUncontrol = """
        SELECT U.idComputers, U.start, U.stop, C.ipaddress
        FROM Uncontrol AS U WITH (NOLOCK)
        INNER JOIN Computers AS C WITH (NOLOCK) ON C.id = U.idComputers
        WHERE U.stop >= DATEADD(second, -300, GETDATE())
        """;

    public const string ClubPseudoComputerAddress = "Club";

    /// <summary>
    /// Latest GCServer heartbeat, without a time window, so "no heartbeat row at all" (deleted by GameClass's
    /// Clear statistics, recreated only at the next operator logon) can be told apart from "heartbeat stale".
    /// </summary>
    public const string LastHeartbeat = """
        SELECT MAX(U.stop) AS HeartbeatAt
        FROM Uncontrol AS U WITH (NOLOCK)
        INNER JOIN Computers AS C WITH (NOLOCK) ON C.id = U.idComputers
        WHERE C.ipaddress = 'Club'
        """;

    public static IEnumerable<string> All =>
        [ServerNow, ClubSettings, Computers, ComputerGroups, Tariffs, TariffVariants, CurrentSegments, RecentUncontrol, LastHeartbeat];
}
