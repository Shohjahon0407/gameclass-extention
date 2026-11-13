using Manajoy.GameClass.Bridge.Configuration;
using Manajoy.GameClass.Bridge.GameClass;
using Manajoy.GameClass.Inspector.Infrastructure;

namespace Manajoy.GameClass.Bridge.Tests;

public class QueriesAndOptionsTests
{
    public static TheoryData<string> AllQueries()
    {
        var data = new TheoryData<string>();
        foreach (var query in GameClassQueries.All)
        {
            data.Add(query);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllQueries))]
    public void Every_bridge_query_passes_the_read_only_guard(string sql)
    {
        Assert.True(ReadOnlyQueryGuard.IsReadOnly(sql, out var reason), reason);
    }

    [Theory]
    [MemberData(nameof(AllQueries))]
    public void Every_bridge_query_avoids_sql2005_only_syntax(string sql)
    {
        foreach (var token in new[] { "ROW_NUMBER", "OFFSET", "FETCH", "TRY_CONVERT", "OUTER APPLY", "CROSS APPLY", "IIF", "FORMAT" })
        {
            Assert.DoesNotMatch(new System.Text.RegularExpressions.Regex($@"\b{token}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase), sql);
        }

        // A CTE would start with WITH; table hints "WITH (NOLOCK)" are fine.
        Assert.DoesNotMatch(new System.Text.RegularExpressions.Regex(@"^\s*WITH\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase), sql);
    }

    [Fact]
    public void Default_options_are_valid()
    {
        Assert.True(new BridgeOptionsValidator().Validate(null, new BridgeOptions()).Succeeded);
    }

    [Fact]
    public void Lan_binding_requires_an_api_key()
    {
        var options = new BridgeOptions { HttpUrls = "http://0.0.0.0:8787" };
        var result = new BridgeOptionsValidator().Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("ApiKey"));

        options.ApiKey = "0123456789abcdef0123456789abcdef";
        Assert.True(new BridgeOptionsValidator().Validate(null, options).Succeeded);
    }

    [Fact]
    public void Sql_login_needs_user_and_password_together()
    {
        var options = new BridgeOptions { SqlLogin = new SqlLoginOptions { UserName = "gcbridge_ro" } };
        Assert.True(new BridgeOptionsValidator().Validate(null, options).Failed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(301)]
    public void Poll_interval_is_bounded(int seconds)
    {
        Assert.True(new BridgeOptionsValidator().Validate(null, new BridgeOptions { PollIntervalSeconds = seconds }).Failed);
    }
}
