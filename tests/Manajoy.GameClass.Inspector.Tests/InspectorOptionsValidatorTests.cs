using Manajoy.GameClass.Inspector.Configuration;

namespace Manajoy.GameClass.Inspector.Tests;

public class InspectorOptionsValidatorTests
{
    private readonly InspectorOptionsValidator _validator = new();

    [Fact]
    public void Defaults_are_valid_and_sampling_is_off()
    {
        var options = new InspectorOptions();

        Assert.True(_validator.Validate(null, options).Succeeded);
        Assert.False(options.AllowSampleRows);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Sample_row_limit_above_hard_limit_is_rejected(int maxRows)
    {
        Assert.True(_validator.Validate(null, new InspectorOptions { MaxSampleRows = maxRows }).Failed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void Connection_timeout_outside_range_is_rejected(int seconds)
    {
        Assert.True(_validator.Validate(null, new InspectorOptions { ConnectionTimeoutSeconds = seconds }).Failed);
    }

    [Fact]
    public void Sql_login_without_password_is_rejected()
    {
        var options = new InspectorOptions { SqlLogin = new SqlLoginOptions { UserName = "reader" } };

        Assert.True(_validator.Validate(null, options).Failed);
    }

    [Fact]
    public void Sample_table_without_name_is_rejected()
    {
        var options = new InspectorOptions { SampleTables = [new SampleTableOptions { Database = "gc" }] };

        Assert.True(_validator.Validate(null, options).Failed);
    }
}
