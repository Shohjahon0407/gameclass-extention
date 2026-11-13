using Microsoft.Extensions.Options;

namespace Manajoy.GameClass.Inspector.Configuration;

internal sealed class InspectorOptionsValidator : IValidateOptions<InspectorOptions>
{
    private const int MaxConnectionTimeoutSeconds = 30;
    private const int MaxCommandTimeoutSeconds = 300;

    public ValidateOptionsResult Validate(string? name, InspectorOptions options)
    {
        var failures = new List<string>();

        if (options.ConnectionTimeoutSeconds is < 1 or > MaxConnectionTimeoutSeconds)
        {
            failures.Add($"ConnectionTimeoutSeconds must be between 1 and {MaxConnectionTimeoutSeconds}.");
        }

        if (options.CommandTimeoutSeconds is < 1 or > MaxCommandTimeoutSeconds)
        {
            failures.Add($"CommandTimeoutSeconds must be between 1 and {MaxCommandTimeoutSeconds}.");
        }

        if (options.MaxSampleRows is < 1 or > InspectorOptions.SampleRowsHardLimit)
        {
            failures.Add($"MaxSampleRows must be between 1 and {InspectorOptions.SampleRowsHardLimit}.");
        }

        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
        {
            failures.Add("OutputDirectory must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(options.LogDirectory))
        {
            failures.Add("LogDirectory must not be empty.");
        }

        if (options.AdditionalServers.Any(string.IsNullOrWhiteSpace))
        {
            failures.Add("AdditionalServers must not contain empty entries.");
        }

        foreach (var table in options.SampleTables)
        {
            if (string.IsNullOrWhiteSpace(table.Database) || string.IsNullOrWhiteSpace(table.Table) || string.IsNullOrWhiteSpace(table.Schema))
            {
                failures.Add($"SampleTables entry '{table}' must specify Database, Schema and Table.");
            }
        }

        var hasUser = !string.IsNullOrWhiteSpace(options.SqlLogin.UserName);
        var hasPassword = !string.IsNullOrEmpty(options.SqlLogin.Password);
        if (hasUser != hasPassword)
        {
            failures.Add("SqlLogin requires both UserName and Password, or neither.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
