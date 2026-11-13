using Manajoy.GameClass.Inspector.Discovery;

namespace Manajoy.GameClass.Inspector.Tests;

public class SqlServerNameTests
{
    [Theory]
    [InlineData("(local)", ".")]
    [InlineData("localhost", ".")]
    [InlineData(".", ".")]
    [InlineData(@".\MSSQLSERVER", ".")]
    [InlineData(@"(local)\SQLEXPRESS", @".\SQLEXPRESS")]
    [InlineData(@"localhost\GAMECLASS", @".\GAMECLASS")]
    [InlineData("localhost,14333", "localhost,14333")]
    [InlineData(@"np:\\.\pipe\sql\query", @"np:\\.\pipe\sql\query")]
    [InlineData(@"OTHER-PC\SQL", @"OTHER-PC\SQL")]
    public void Local_aliases_are_normalized(string input, string expected)
    {
        Assert.Equal(expected, SqlServerName.Normalize(input));
    }

    [Fact]
    public void Machine_name_is_treated_as_local()
    {
        Assert.Equal(@".\GC", SqlServerName.Normalize($@"{Environment.MachineName}\GC"));
    }

    [Theory]
    [InlineData("MSSQLSERVER", ".")]
    [InlineData("GAMECLASS", @".\GAMECLASS")]
    public void Instance_names_map_to_server_names(string instance, string expected)
    {
        Assert.Equal(expected, SqlServerName.ForInstance(instance));
    }

    [Theory]
    [InlineData(".", "MSSQLSERVER")]
    [InlineData(@".\GAMECLASS", "GAMECLASS")]
    public void Server_names_map_back_to_instance_names(string server, string expected)
    {
        Assert.Equal(expected, SqlServerName.InstanceNameOf(server));
    }

    [Theory]
    [InlineData("8.00.2039", 8)]
    [InlineData("16.0.1000.6", 16)]
    [InlineData(null, null)]
    [InlineData("garbage", null)]
    public void Major_version_is_parsed(string? version, int? expected)
    {
        Assert.Equal(expected, SqlVersion.ParseMajor(version));
    }
}
