using System.Text.RegularExpressions;

namespace Manajoy.GameClass.Inspector.Analysis;

internal static partial class NameTokenizer
{
    // Splits "tblComputerGroups", "COMP_STATE", "PCList", "Сеансы2" into words:
    // acronym before a capitalised word | capitalised/lower word | acronym | number.
    [GeneratedRegex(@"\p{Lu}+(?=\p{Lu}\p{Ll})|\p{Lu}?\p{Ll}+|\p{Lu}+|\p{Nd}+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    public static IReadOnlyList<string> Tokenize(string name) =>
        TokenRegex().Matches(name).Select(match => match.Value.ToLowerInvariant()).ToList();

    /// <summary>Lower-case name with separators removed, for keywords hidden in unsplittable names ("tblcomputers").</summary>
    public static string Compact(string name) =>
        new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
