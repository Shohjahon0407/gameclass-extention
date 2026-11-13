using System.Globalization;

namespace Manajoy.GameClass.Inspector.Discovery;

internal static class SqlVersion
{
    public const int SqlServer2000 = 8;

    public static int? ParseMajor(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var major = version.Split('.', 2)[0];
        return int.TryParse(major, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    public static string? FamilyName(int? major) => major switch
    {
        7 => "SQL Server 7.0 / MSDE 1.0",
        8 => "SQL Server 2000 / MSDE 2000",
        9 => "SQL Server 2005",
        10 => "SQL Server 2008 / 2008 R2",
        11 => "SQL Server 2012",
        12 => "SQL Server 2014",
        13 => "SQL Server 2016",
        14 => "SQL Server 2017",
        15 => "SQL Server 2019",
        16 => "SQL Server 2022",
        17 => "SQL Server 2025",
        null => null,
        _ => $"SQL Server (major version {major})",
    };
}
