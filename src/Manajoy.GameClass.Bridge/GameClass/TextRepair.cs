using System.Text;

namespace Manajoy.GameClass.Bridge.GameClass;

/// <summary>
/// Some GameClass installs store Russian names in nvarchar columns as cp1252 mojibake of cp1251 text
/// (the installer ran under a Western ANSI code page), e.g. "Ïî óìîë÷àíèþ" instead of "По умолчанию".
/// This reverses that one specific corruption and leaves every other string untouched.
/// </summary>
internal static class TextRepair
{
    private static readonly Encoding Cp1252;
    private static readonly Encoding Cp1251;

    static TextRepair()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp1252 = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        Cp1251 = Encoding.GetEncoding(1251);
    }

    public static string FixCyrillicMojibake(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Any(c => c is >= 'Ѐ' and <= 'ӿ'))
        {
            return value;
        }

        // cp1251 Russian read as cp1252 is mostly Latin-1 letters À..ÿ. Requiring them to dominate keeps
        // genuine Western names such as "Café Zürich" unchanged.
        var latin1Letters = value.Count(c => c is >= 'À' and <= 'ÿ');
        var letters = value.Count(char.IsLetter);
        if (latin1Letters < 2 || latin1Letters * 2 < letters)
        {
            return value;
        }

        try
        {
            return Cp1251.GetString(Cp1252.GetBytes(value));
        }
        catch (EncoderFallbackException)
        {
            // Contains characters that cp1252 cannot represent, so it is not this kind of mojibake.
            return value;
        }
    }
}
