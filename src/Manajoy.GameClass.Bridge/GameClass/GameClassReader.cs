using Manajoy.GameClass.Bridge.Data;

namespace Manajoy.GameClass.Bridge.GameClass;

/// <summary>Reads one consistent-enough round of GameClass data over a single connection.</summary>
internal sealed class GameClassReader(ReadOnlyGameClassDb db)
{
    public Task<GameClassData> ReadAsync(CancellationToken cancellationToken) =>
        db.WithConnectionAsync(async scope =>
        {
            var (now, utcOffset) = (await scope.QueryAsync(GameClassQueries.ServerNow,
                r => (Now: r.GetDateTimeOrNull("ServerNow") ?? DateTime.Now,
                      Offset: TimeSpan.FromMinutes(r.GetInt32OrNull("UtcOffsetMinutes") ?? 0)), cancellationToken))[0];

            var settings = await scope.QueryAsync(GameClassQueries.ClubSettings,
                r => (Key: r.GetStringOrEmpty("SettingKey"), Value: r.GetStringOrNull("SettingValue")), cancellationToken);

            var computers = await scope.QueryAsync(GameClassQueries.Computers, r => new ComputerRow(
                r.GetInt32("id"),
                r.GetInt32("number"),
                r.GetStringOrEmpty("ipaddress"),
                r.GetInt32OrNull("idGroup") ?? 0,
                (r.GetInt32OrNull("vip") ?? 0) != 0,
                r.GetInt32OrNull("client_type") ?? 0,
                (r.GetInt32OrNull("ignore_offline") ?? 0) != 0), cancellationToken);

            var groups = await scope.QueryAsync(GameClassQueries.ComputerGroups,
                r => new ComputerGroupRow(r.GetInt32("id"), TextRepair.FixCyrillicMojibake(r.GetStringOrEmpty("name"))), cancellationToken);

            var tariffs = await scope.QueryAsync(GameClassQueries.Tariffs, r => new TariffRow(
                r.GetInt32("id"),
                TextRepair.FixCyrillicMojibake(r.GetStringOrEmpty("name")),
                (r.GetInt32OrNull("internet") ?? 0) != 0,
                r.GetInt32OrNull("idGroup") ?? 0,
                r.GetDecimalOrNull("roundmoney") ?? 0m,
                r.GetInt32OrNull("roundtime") ?? 0,
                r.GetInt32OrNull("priorityshow") ?? 0,
                r.GetInt32OrNull("useseparatesumm") ?? 0,
                r.GetDecimalOrNull("startmoneymin") ?? 0m,
                r.GetDecimalOrNull("startmoneymax") ?? 0m,
                r.GetDecimalOrNull("maximumtrust") ?? 0m), cancellationToken);

            var variants = await scope.QueryAsync(GameClassQueries.TariffVariants, r => new TariffVariantRow(
                r.GetInt32("id"),
                r.GetInt32("idTarifs"),
                TextRepair.FixCyrillicMojibake(r.GetStringOrEmpty("name")),
                r.GetDateTimeOrNull("start") ?? DateTime.MinValue,
                r.GetDateTimeOrNull("stop") ?? DateTime.MinValue,
                r.GetDecimalOrNull("cost") ?? 0m,
                (r.GetInt32OrNull("ispacket") ?? 0) != 0,
                r.GetStringOrEmpty("daysofweek"),
                r.GetStringOrNull("condition"),
                (r.GetInt32OrNull("FreePacket") ?? 0) != 0), cancellationToken);

            var segments = await scope.QueryAsync(GameClassQueries.CurrentSegments, r => new SessionSegmentRow(
                r.GetInt32("SessionId"),
                r.GetInt32("SegmentId"),
                r.GetInt32("ComputerId"),
                r.GetInt32OrNull("AccountId") ?? 0,
                r.GetDateTimeOrNull("Started") ?? DateTime.MinValue,
                r.GetDateTimeOrNull("SegmentStart") ?? DateTime.MinValue,
                r.GetDateTimeOrNull("SegmentStop") ?? DateTime.MinValue,
                r.GetInt32OrNull("TariffId") ?? 0,
                (r.GetInt32OrNull("Whole") ?? 0) != 0,
                (r.GetInt32OrNull("Postpay") ?? 0) != 0,
                r.GetInt32OrNull("SessionState") ?? 0,
                r.GetInt32OrNull("SessionStatus") ?? 0,
                r.GetDecimalOrNull("CommonPay") ?? 0m,
                r.GetDecimalOrNull("SeparateTrafficPay") ?? 0m,
                r.GetDecimalOrNull("PrintCost") ?? 0m,
                r.GetDecimalOrNull("ServiceCost") ?? 0m,
                r.GetInt32OrNull("AdditionalMinutes") ?? 0,
                r.GetDoubleOrNull("Traffic") ?? 0d,
                r.GetDecimalOrNull("TimeCost") ?? 0m,
                r.GetDecimalOrNull("SessionTimeCost") ?? 0m), cancellationToken);

            var uncontrolled = await scope.QueryAsync(GameClassQueries.RecentUncontrol, r => new UncontrolRow(
                r.GetInt32("idComputers"),
                r.GetDateTimeOrNull("start") ?? DateTime.MinValue,
                r.GetDateTimeOrNull("stop") ?? DateTime.MinValue,
                string.Equals(r.GetStringOrNull("ipaddress"), GameClassQueries.ClubPseudoComputerAddress, StringComparison.OrdinalIgnoreCase)),
                cancellationToken);

            var heartbeat = (await scope.QueryAsync(GameClassQueries.LastHeartbeat,
                r => r.GetDateTimeOrNull("HeartbeatAt"), cancellationToken)).FirstOrDefault();

            string? Setting(string key) => settings.FirstOrDefault(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;
            var club = new ClubSettings(
                Setting("ClubName"),
                Setting("Currency"),
                int.TryParse(Setting("ClientQueryTime"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var queryTime) ? queryTime : null,
                Setting("WriteUnControlClub")?.Trim() != "0",
                RegistryNumber(Setting("StartMoneyMinimum")),
                RegistryNumber(Setting("StartMoneyMaximum")),
                RegistryNumber(Setting("MaximumTrustPostPay")));

            return new GameClassData(now, utcOffset, club, heartbeat, computers, groups, tariffs, variants, segments, uncontrolled);
        }, cancellationToken);

    /// <summary>GameClass writes Registry numbers with the Windows decimal separator ("0,5" on a Russian system).</summary>
    internal static decimal? RegistryNumber(string? value) =>
        decimal.TryParse(value?.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
}
