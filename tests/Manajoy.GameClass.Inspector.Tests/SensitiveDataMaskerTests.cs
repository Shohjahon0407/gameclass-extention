using Manajoy.GameClass.Inspector.Inspection;

namespace Manajoy.GameClass.Inspector.Tests;

public class SensitiveDataMaskerTests
{
    [Theory]
    [InlineData("Password")]
    [InlineData("PwdHash")]
    [InlineData("pass_word")]
    [InlineData("PASSWD")]
    [InlineData("Phone")]
    [InlineData("MobilePhone")]
    [InlineData("Telephone")]
    [InlineData("Tel")]
    [InlineData("EMail")]
    [InlineData("PinCode")]
    [InlineData("CardNumber")]
    [InlineData("BirthDate")]
    [InlineData("LastName")]
    [InlineData("Пароль")]
    [InlineData("Телефон")]
    [InlineData("Фамилия")]
    [InlineData("ФИО")]
    [InlineData("Имя")]
    [InlineData("tblUserPasswords")]
    [InlineData("HomeAddress")]
    [InlineData("EmailAddress")]
    [InlineData("Адрес")]
    public void Sensitive_columns_are_detected(string column)
    {
        Assert.True(SensitiveDataMasker.IsSensitiveColumn(column));
    }

    [Theory]
    [InlineData("Id")]
    [InlineData("ComputerId")]
    [InlineData("Number")]
    [InlineData("IP")]
    [InlineData("IPAddress")]
    [InlineData("ip_address")]
    [InlineData("MacAddr")]
    [InlineData("State")]
    [InlineData("StartTime")]
    [InlineData("TariffName")]
    [InlineData("Shipping")]
    [InlineData("Spinner")]
    [InlineData("Hotel")]
    public void Technical_columns_are_not_masked(string column)
    {
        Assert.False(SensitiveDataMasker.IsSensitiveColumn(column));
    }

    [Theory]
    [InlineData("Clients", true)]
    [InlineData("Operators", true)]
    [InlineData("Клиенты", true)]
    [InlineData("Computers", false)]
    [InlineData("Tariffs", false)]
    public void Person_tables_are_detected(string table, bool expected)
    {
        Assert.Equal(expected, SensitiveDataMasker.IsPersonTable(table));
    }

    [Theory]
    [InlineData("john@example.com")]
    [InlineData("+998 90 123-45-67")]
    [InlineData("4111111111111111")]
    public void Personal_looking_values_are_masked(string value)
    {
        Assert.Equal(SensitiveDataMasker.Mask, SensitiveDataMasker.MaskValue(value));
    }

    [Fact]
    public void Ordinary_values_pass_through_and_long_values_are_truncated()
    {
        Assert.Equal("192.168.1.10", SensitiveDataMasker.MaskValue("192.168.1.10"));
        Assert.Equal("42", SensitiveDataMasker.MaskValue(42));
        Assert.Null(SensitiveDataMasker.MaskValue(DBNull.Value));

        var truncated = SensitiveDataMasker.MaskValue(new string('a', 500));
        Assert.Equal(SensitiveDataMasker.MaxValueLength + 1, truncated!.Length);
    }
}
