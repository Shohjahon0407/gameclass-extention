using Microsoft.Extensions.Options;

namespace Manajoy.GameClass.Bridge.Configuration;

internal sealed class BridgeOptionsValidator : IValidateOptions<BridgeOptions>
{
    private const int MaxConnectionTimeoutSeconds = 30;
    private const int MaxCommandTimeoutSeconds = 120;
    private const int MinPollIntervalSeconds = 1;
    private const int MaxPollIntervalSeconds = 300;

    public ValidateOptionsResult Validate(string? name, BridgeOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Server))
        {
            failures.Add("Server must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(options.Database))
        {
            failures.Add("Database must not be empty.");
        }

        if (options.ConnectionTimeoutSeconds is < 1 or > MaxConnectionTimeoutSeconds)
        {
            failures.Add($"ConnectionTimeoutSeconds must be between 1 and {MaxConnectionTimeoutSeconds}.");
        }

        if (options.CommandTimeoutSeconds is < 1 or > MaxCommandTimeoutSeconds)
        {
            failures.Add($"CommandTimeoutSeconds must be between 1 and {MaxCommandTimeoutSeconds}.");
        }

        if (options.PollIntervalSeconds is < MinPollIntervalSeconds or > MaxPollIntervalSeconds)
        {
            failures.Add($"PollIntervalSeconds must be between {MinPollIntervalSeconds} and {MaxPollIntervalSeconds}.");
        }

        if (options.MaxEventsInMemory is < 1 or > 100_000)
        {
            failures.Add("MaxEventsInMemory must be between 1 and 100000.");
        }

        var hasUser = !string.IsNullOrWhiteSpace(options.SqlLogin.UserName);
        var hasPassword = !string.IsNullOrEmpty(options.SqlLogin.Password);
        if (hasUser != hasPassword)
        {
            failures.Add("SqlLogin requires both UserName and Password, or neither.");
        }

        var urls = SplitUrls(options.HttpUrls);
        if (urls.Count == 0)
        {
            failures.Add("HttpUrls must contain at least one URL.");
        }

        foreach (var url in urls)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                failures.Add($"HttpUrls entry '{url}' is not a valid http(s) URL.");
            }
            else if (!IsLoopback(uri) && string.IsNullOrWhiteSpace(options.ApiKey))
            {
                failures.Add($"ApiKey is required when the API listens on a non-loopback address ('{url}').");
            }
        }

        if (!string.IsNullOrEmpty(options.ApiKey) && options.ApiKey.Trim().Length < BridgeOptions.MinApiKeyLength)
        {
            failures.Add($"ApiKey must be at least {BridgeOptions.MinApiKeyLength} characters (use a random value).");
        }

        ValidatePush(options.Push, failures);
        ValidateAnalytics(options.Analytics, failures);

        if (string.IsNullOrWhiteSpace(options.SnapshotFile) || string.IsNullOrWhiteSpace(options.EventLogFile) || string.IsNullOrWhiteSpace(options.LogDirectory))
        {
            failures.Add("SnapshotFile, EventLogFile and LogDirectory must not be empty.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateAnalytics(AnalyticsPushOptions analytics, List<string> failures)
    {
        if (analytics.IntervalMinutes is < 1 or > 1440)
        {
            failures.Add("Analytics:IntervalMinutes must be between 1 and 1440.");
        }

        if (analytics.BatchDays is < 1 or > 31)
        {
            failures.Add("Analytics:BatchDays must be between 1 and 31.");
        }

        if (analytics.CommandTimeoutSeconds is < 5 or > 600)
        {
            failures.Add("Analytics:CommandTimeoutSeconds must be between 5 and 600.");
        }
    }

    private static void ValidatePush(PushOptions push, List<string> failures)
    {
        if (!push.IsEnabled)
        {
            return;
        }

        if (!Uri.TryCreate(push.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            failures.Add("Push:Url must be an absolute http(s) URL.");
            return;
        }

        if (uri.Scheme == "http" && !IsLoopback(uri) && !IsPrivateAddress(uri))
        {
            failures.Add("Push:Url must use https unless it points to a loopback or private LAN address.");
        }

        if (string.IsNullOrWhiteSpace(push.Key))
        {
            failures.Add("Push:Key is required when Push:Url is set (install-service.ps1 -PushKey, or Bridge__Push__Key).");
        }

        if (push.IntervalSeconds is < 5 or > 300)
        {
            failures.Add("Push:IntervalSeconds must be between 5 and 300.");
        }

        if (push.TimeoutSeconds is < 1 or > 60)
        {
            failures.Add("Push:TimeoutSeconds must be between 1 and 60.");
        }
    }

    private static bool IsPrivateAddress(Uri uri)
    {
        if (!System.Net.IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip))
        {
            return false;
        }

        var b = ip.GetAddressBytes();
        return ip.IsIPv6UniqueLocal || ip.IsIPv6LinkLocal
            || (b.Length == 4 && (b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168)));
    }

    public static IReadOnlyList<string> SplitUrls(string? urls) =>
        (urls ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool IsLoopback(Uri uri) =>
        uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
}
