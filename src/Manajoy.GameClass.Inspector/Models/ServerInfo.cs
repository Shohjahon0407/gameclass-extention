using Manajoy.GameClass.Inspector.Discovery;

namespace Manajoy.GameClass.Inspector.Models;

/// <summary>Facts reported by a SQL Server instance after a successful connection.</summary>
public sealed record ServerInfo
{
    public string? ServerName { get; init; }

    public string? ProductVersion { get; init; }

    public string? ProductLevel { get; init; }

    public string? Edition { get; init; }

    public int? EngineEdition { get; init; }

    public string? CurrentLogin { get; init; }

    public int? MajorVersion => SqlVersion.ParseMajor(ProductVersion);

    public string? ProductFamily => SqlVersion.FamilyName(MajorVersion);
}
