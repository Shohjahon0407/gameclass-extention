namespace Manajoy.GameClass.Inspector.Analysis;

internal enum DomainCategory
{
    Computers,
    Sessions,
    Tariffs,
    Clients,
    Reservations,
    Operators,
    Groups,
    Payments,
}

/// <summary>
/// Vocabulary of a gaming-club domain in English, Russian and Russian transliteration.
/// Heuristic only: it describes what club software tends to store, not any specific schema.
/// </summary>
internal static class DomainKeywords
{
    private static readonly Dictionary<DomainCategory, KeywordSet> Categories = new()
    {
        [DomainCategory.Computers] = new(
            Prefix("computer", "host", "terminal", "machine", "workstation", "компьют", "kompyut", "komputer"),
            Exact("comp", "comps", "pc", "pcs", "station", "stations", "комп", "пк", "seat", "seats")),
        [DomainCategory.Sessions] = new(
            Prefix("session", "seans", "сеанс", "сесси", "visit"),
            Exact("sess")),
        [DomainCategory.Tariffs] = new(
            Prefix("tarif", "тариф", "price", "pricing", "прайс", "расценк"),
            Exact("rate", "rates")),
        [DomainCategory.Clients] = new(
            Prefix("client", "klient", "клиент", "customer", "member", "account", "пользоват", "абонент", "gamer", "player", "guest"),
            Exact("user", "users", "usr")),
        [DomainCategory.Reservations] = new(
            Prefix("reserv", "booking", "bron", "брон", "резерв"),
            Exact("book", "books")),
        [DomainCategory.Operators] = new(
            Prefix("operator", "оператор", "admin", "админ", "manager", "менеджер", "cashier", "кассир", "shift", "смен", "staff", "employee", "сотрудник"),
            Exact()),
        [DomainCategory.Groups] = new(
            Prefix("group", "grupp", "групп", "зон"),
            Exact("grp", "zone", "zones", "hall", "halls", "zal", "зал", "залы", "room", "rooms")),
        [DomainCategory.Payments] = new(
            Prefix("balance", "баланс", "payment", "oplat", "оплат", "money", "cash", "касс", "deposit", "bonus", "бонус", "invoice", "счет", "счёт", "transact", "платеж", "платёж"),
            Exact("pay", "paid")),
    };

    private static readonly KeywordSet NetworkColumns = new(
        Prefix("ipaddr", "ipv4", "macaddr", "hostname", "netbios"),
        Exact("ip", "mac"));

    private static readonly KeywordSet TimeColumns = new(
        Prefix("start", "begin", "finish", "stop", "начал", "конец", "оконч", "время"),
        Exact("end", "time", "from", "till", "until"));

    private static readonly KeywordSet PriceColumns = new(
        Prefix("price", "cost", "amount", "summ", "цен", "стоим", "сумм"),
        Exact("sum", "rate"));

    private static readonly KeywordSet GameClassNames = new(
        Prefix("gameclass"),
        Exact("gc", "gc3"));

    public static IEnumerable<DomainCategory> AllCategories => Categories.Keys;

    public static IReadOnlyList<DomainCategory> Match(string name)
    {
        var parsed = new ParsedName(name);
        return Categories.Where(category => category.Value.Matches(parsed)).Select(category => category.Key).ToList();
    }

    public static bool IsNetworkColumn(string name) => NetworkColumns.Matches(new ParsedName(name));

    public static bool IsTimeColumn(string name) => TimeColumns.Matches(new ParsedName(name));

    public static bool IsPriceColumn(string name) => PriceColumns.Matches(new ParsedName(name));

    public static bool MentionsGameClass(string name)
    {
        var parsed = new ParsedName(name);
        return GameClassNames.Matches(parsed) || (parsed.Tokens.Contains("game") && parsed.Tokens.Contains("class"));
    }

    public static string Describe(DomainCategory category) => category switch
    {
        DomainCategory.Computers => "computer/PC",
        DomainCategory.Sessions => "session",
        DomainCategory.Tariffs => "tariff/price",
        DomainCategory.Clients => "client/user",
        DomainCategory.Reservations => "reservation/booking",
        DomainCategory.Operators => "operator/shift",
        DomainCategory.Groups => "group/zone",
        DomainCategory.Payments => "balance/payment",
        _ => category.ToString().ToLowerInvariant(),
    };

    private static string[] Prefix(params string[] keywords) => keywords;

    private static string[] Exact(params string[] keywords) => keywords;

    private sealed record ParsedName
    {
        public ParsedName(string name)
        {
            Tokens = NameTokenizer.Tokenize(name);
            Compact = NameTokenizer.Compact(name);
        }

        public IReadOnlyList<string> Tokens { get; }

        public string Compact { get; }
    }

    /// <summary>
    /// Prefix keywords match the start of a word ("computers", "tariffs") and, when long enough, anywhere
    /// in the compacted name ("tblcomputers"). Exact keywords are short words that must match a whole word,
    /// so "pc" does not match "spec" and "comp" does not match "company".
    /// </summary>
    private sealed class KeywordSet(string[] prefixes, string[] exact)
    {
        private const int MinCompactMatchLength = 5;

        public bool Matches(ParsedName name) =>
            name.Tokens.Any(token =>
                exact.Contains(token, StringComparer.Ordinal)
                || prefixes.Any(prefix => token.StartsWith(prefix, StringComparison.Ordinal)))
            || prefixes.Any(prefix => prefix.Length >= MinCompactMatchLength && name.Compact.Contains(prefix, StringComparison.Ordinal));
    }
}
