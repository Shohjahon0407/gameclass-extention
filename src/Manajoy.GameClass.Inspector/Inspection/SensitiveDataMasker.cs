using System.Globalization;
using System.Text.RegularExpressions;
using Manajoy.GameClass.Inspector.Analysis;

namespace Manajoy.GameClass.Inspector.Inspection;

/// <summary>
/// Decides which columns must never be read and scrubs personal-looking values from the rest.
/// Errs on the side of masking: a hidden technical column costs little, a leaked phone number does not.
/// </summary>
internal static partial class SensitiveDataMasker
{
    public const string Mask = "***MASKED***";
    public const int MaxValueLength = 100;

    // Whole-word or word-prefix matches (short fragments must be whole words: "pin" ≠ "shipping").
    private static readonly string[] SensitivePrefixes =
    [
        "pass", "pwd", "парол", "parol", "hash", "salt", "secret", "token", "credential",
        "card", "cvv", "cvc", "iban", "phone", "telephone", "mobile", "телефон", "email", "mail", "почт",
        "passport", "паспорт", "address", "adres", "адрес", "birth", "рожд", "surname", "lastname",
        "firstname", "middlename", "фамил", "отчеств", "snils", "снилс",
    ];

    private static readonly string[] SensitiveWords = ["pin", "tel", "dob", "fio", "фио", "имя", "inn", "инн", "ssn"];

    private static readonly HashSet<string> TextTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "char", "varchar", "nchar", "nvarchar", "text", "ntext",
    };

    private static readonly HashSet<string> UnreadableTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "binary", "varbinary", "image", "timestamp", "rowversion", "text", "ntext", "xml",
        "sql_variant", "geography", "geometry", "hierarchyid",
    };

    private const int MinCompactMatchLength = 5;

    [GeneratedRegex(@"[^@\s]+@[^@\s]+\.[^@\s]+", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    // Ten or more digits, optionally separated: phone and card numbers.
    [GeneratedRegex(@"\+?\d(?:[\s\-()]*\d){9,}", RegexOptions.CultureInvariant)]
    private static partial Regex LongNumberRegex();

    [GeneratedRegex("(ip|mac)(v4|v6)?(address|addr|adres)", RegexOptions.CultureInvariant)]
    private static partial Regex NetworkAddressRegex();

    private static readonly HashSet<string> NetworkQualifiers = new(StringComparer.Ordinal) { "ip", "mac", "ipv4", "ipv6" };

    public static bool IsSensitiveColumn(string columnName)
    {
        var tokens = NameTokenizer.Tokenize(columnName).ToList();
        var compact = NameTokenizer.Compact(columnName);

        // "IPAddress" / "MacAddr" describe club hardware, not a person's home address.
        if (tokens.Any(token => NetworkQualifiers.Contains(token)))
        {
            tokens.RemoveAll(token => token.StartsWith("addr", StringComparison.Ordinal) || token.StartsWith("adres", StringComparison.Ordinal));
            compact = NetworkAddressRegex().Replace(compact, string.Empty);
        }

        return tokens.Any(token =>
                   SensitiveWords.Contains(token, StringComparer.Ordinal)
                   || SensitivePrefixes.Any(prefix => token.StartsWith(prefix, StringComparison.Ordinal)))
               || SensitivePrefixes.Any(prefix => prefix.Length >= MinCompactMatchLength && compact.Contains(prefix, StringComparison.Ordinal));
    }

    /// <summary>Tables that hold people (clients, operators): every text column in them is masked.</summary>
    public static bool IsPersonTable(string tableName) =>
        DomainKeywords.Match(tableName).Any(category => category is DomainCategory.Clients or DomainCategory.Operators);

    public static bool IsTextType(string dataType) => TextTypes.Contains(dataType);

    public static bool IsUnreadableType(string dataType) => UnreadableTypes.Contains(dataType);

    public static string? MaskValue(object? value)
    {
        var text = value switch
        {
            null or DBNull => null,
            byte[] => "<binary>",
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };

        if (text is null)
        {
            return null;
        }

        if (value is string && (EmailRegex().IsMatch(text) || LongNumberRegex().IsMatch(text)))
        {
            return Mask;
        }

        return text.Length > MaxValueLength ? string.Concat(text.AsSpan(0, MaxValueLength), "…") : text;
    }
}
