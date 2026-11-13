using Manajoy.GameClass.Bridge.Configuration;
using Manajoy.GameClass.Bridge.GameClass;
using Manajoy.GameClass.Bridge.Hosting;

namespace Manajoy.GameClass.Bridge.Tests;

public class TextRepairAndHostsTests
{
    [Theory]
    [InlineData("Ïî óìîë÷àíèþ", "По умолчанию")]
    [InlineData("íî÷ü ïî÷àñîâàÿ", "ночь почасовая")]
    [InlineData("Internet", "Internet")]
    [InlineData("По умолчанию", "По умолчанию")]
    [InlineData("Café Zürich", "Café Zürich")]
    [InlineData("VIP Tariff", "VIP Tariff")]
    [InlineData("", "")]
    public void Repairs_only_cp1252_mojibake_of_cyrillic(string stored, string expected)
    {
        Assert.Equal(expected, TextRepair.FixCyrillicMojibake(stored));
    }

    [Fact]
    public void Loopback_binding_restricts_allowed_hosts()
    {
        var hosts = BridgeHosts.AllowedHosts(["http://127.0.0.1:8787"]).Split(";").Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["127.0.0.1", "[::1]", "localhost"], hosts);
    }

    [Fact]
    public void Named_lan_binding_adds_its_host()
    {
        Assert.Contains("192.168.1.10", BridgeHosts.AllowedHosts(["http://192.168.1.10:8787"]).Split(';'));
    }

    [Theory]
    [InlineData("http://0.0.0.0:8787")]
    [InlineData("http://+:8787")]
    [InlineData("http://*:8787")]
    public void Wildcard_binding_cannot_be_host_filtered(string url)
    {
        Assert.Equal("*", BridgeHosts.AllowedHosts([url]));
    }

    [Fact]
    public void Short_api_key_is_rejected()
    {
        var options = new BridgeOptions { HttpUrls = "http://0.0.0.0:8787", ApiKey = "short" };
        Assert.True(new BridgeOptionsValidator().Validate(null, options).Failed);
    }
}
