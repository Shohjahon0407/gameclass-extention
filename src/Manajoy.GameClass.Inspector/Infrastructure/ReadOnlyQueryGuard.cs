using System.Text.RegularExpressions;

namespace Manajoy.GameClass.Inspector.Infrastructure;

internal sealed class ReadOnlyViolationException(string message) : InvalidOperationException(message);

/// <summary>
/// Last line of defence for the read-only guarantee: rejects any statement that is not a single SELECT
/// or that contains a keyword capable of changing data, schema, configuration or server state.
/// </summary>
internal static partial class ReadOnlyQueryGuard
{
    // Comments, [quoted identifiers], 'string literals' and "quoted identifiers" are blanked before
    // scanning, so a column called [Update] or a literal 'BASE TABLE' cannot trigger a false match.
    [GeneratedRegex(@"--[^\r\n]*|/\*[\s\S]*?\*/|\[(?:[^\]]|\]\])*\]|N?'(?:[^']|'')*'|""(?:[^""]|"""")*""")]
    private static partial Regex NonCodeRegex();

    [GeneratedRegex(
        @"\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|CREATE|TRUNCATE|EXEC|EXECUTE|GRANT|REVOKE|DENY|BACKUP|RESTORE|DBCC|SHUTDOWN|KILL|RECONFIGURE|INTO|OPENROWSET|OPENQUERY|OPENDATASOURCE|OPENXML|BULK|WRITETEXT|UPDATETEXT|USE|SET|DECLARE|BEGIN|COMMIT|ROLLBACK|SAVE|TRAN|TRANSACTION|WAITFOR|GOTO|CHECKPOINT|READTEXT|ENABLE|DISABLE)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ForbiddenKeywordRegex();

    [GeneratedRegex(@"\b(sp|xp)_\w*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SystemProcedureRegex();

    [GeneratedRegex(@"^\s*SELECT\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StartsWithSelectRegex();

    public static void EnsureReadOnly(string sql)
    {
        if (!IsReadOnly(sql, out var reason))
        {
            throw new ReadOnlyViolationException($"Blocked by read-only guard: {reason}");
        }
    }

    public static bool IsReadOnly(string sql, out string reason)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            reason = "empty statement";
            return false;
        }

        var code = NonCodeRegex().Replace(sql, " ");

        if (!StartsWithSelectRegex().IsMatch(code))
        {
            reason = "statement must start with SELECT";
            return false;
        }

        if (code.Contains(';', StringComparison.Ordinal))
        {
            reason = "multiple statements are not allowed";
            return false;
        }

        var forbidden = ForbiddenKeywordRegex().Match(code);
        if (forbidden.Success)
        {
            reason = $"forbidden keyword '{forbidden.Value.ToUpperInvariant()}'";
            return false;
        }

        var procedure = SystemProcedureRegex().Match(code);
        if (procedure.Success)
        {
            reason = $"system procedure reference '{procedure.Value}'";
            return false;
        }

        // Unterminated quotes/brackets would leave text the scanner could not classify.
        if (code.IndexOfAny(['\'', '[', '"']) >= 0 || code.Contains("/*", StringComparison.Ordinal))
        {
            reason = "unbalanced quotes, brackets or comments";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
