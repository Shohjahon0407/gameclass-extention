using System.Security.Cryptography;
using System.Text.Json;
using Manajoy.GameClass.Bridge.Setup;

namespace Manajoy.GameClass.Bridge.Analytics;

public sealed record AnalyticsStatus(bool PinSet, bool Unlocked, int DayStartHour, int LockedSeconds);

public sealed record AnalyticsUnlockResult(string Token, int ExpiresInSeconds);

public sealed record AnalyticsPinRequest(string? Pin);

public sealed record AnalyticsPinChangeRequest(string? OldPin, string? NewPin);

public sealed record AnalyticsSettingsRequest(int DayStartHour);

/// <summary>
/// The «Hisobot» lock: a PIN the owner sets, so the cashier who uses the same window does not see the money.
/// analytics.json (%ProgramData%\Manajoy\GameClassBridge\analytics, Administrators and SYSTEM only) keeps a
/// PBKDF2 hash of the PIN and the hour the business day starts; demo keeps both in memory. Unlocking gives a
/// token that lives only in this process and expires after 15 minutes without use. Forgotten PIN: a Windows
/// administrator deletes analytics.json (FIELD-GUIDE).
/// </summary>
internal sealed class AnalyticsAccess(string? path, TimeProvider? clock = null)
{
    public const string TokenHeader = "X-Analytics-Token";
    public const int DefaultDayStartHour = 6;
    public const int MaxAttempts = 5;

    public static string DefaultPath => Path.Combine(SetupEnvironment.DataDirectory, "analytics", "analytics.json");

    /// <summary>For the service's report push: the owner's day start, 06:00 when the file is missing or unreadable.</summary>
    public static int ReadDayStartHour(string? path)
    {
        try
        {
            return new AnalyticsAccess(path).DayStartHour;
        }
        catch (SetupException)
        {
            return DefaultDayStartHour;
        }
    }

    private const int Iterations = 200_000;
    private static readonly TimeSpan TokenLife = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan Lockout = TimeSpan.FromMinutes(5);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly object _sync = new();
    private readonly Dictionary<string, DateTimeOffset> _tokens = [];
    private AnalyticsFile? _memory;
    private int _failures;
    private DateTimeOffset? _lockedUntil;

    public AnalyticsStatus Status(string? token)
    {
        lock (_sync)
        {
            var file = Load();
            return new AnalyticsStatus(file.Hash is not null, file.Hash is not null && Touch(token), file.DayStartHour, LockedSeconds());
        }
    }

    public int DayStartHour
    {
        get
        {
            lock (_sync)
            {
                return Load().DayStartHour;
            }
        }
    }

    public AnalyticsUnlockResult Unlock(AnalyticsPinRequest request)
    {
        lock (_sync)
        {
            var file = Load();
            if (file.Hash is null)
            {
                throw new SetupException("pin_not_set");
            }

            Verify(file, request.Pin);
            return Issue();
        }
    }

    /// <summary>The first PIN needs no old one; changing it needs the old PIN (a stolen token is not enough).</summary>
    public AnalyticsUnlockResult SetPin(AnalyticsPinChangeRequest request)
    {
        lock (_sync)
        {
            var file = Load();
            if (file.Hash is not null)
            {
                Verify(file, request.OldPin);
            }

            if (!IsValidPin(request.NewPin))
            {
                throw new SetupException("pin_format");
            }

            var salt = RandomNumberGenerator.GetBytes(16);
            Save(file with { Salt = Convert.ToBase64String(salt), Hash = Convert.ToBase64String(Hash(request.NewPin!, salt, file.Iterations)) });
            _tokens.Clear();
            return Issue();
        }
    }

    public AnalyticsStatus SetDayStart(AnalyticsSettingsRequest request, string? token)
    {
        lock (_sync)
        {
            Require(token);
            if (request.DayStartHour is < 0 or > 23)
            {
                throw new SetupException("analytics_settings");
            }

            Save(Load() with { DayStartHour = request.DayStartHour });
        }

        return Status(token);
    }

    public void Require(string? token)
    {
        lock (_sync)
        {
            if (Load().Hash is null)
            {
                throw new SetupException("pin_not_set");
            }

            if (!Touch(token))
            {
                throw new SetupException("pin_required");
            }
        }
    }

    public static bool IsValidPin(string? pin) => pin is { Length: >= 4 and <= 8 } && pin.All(char.IsAsciiDigit);

    private void Verify(AnalyticsFile file, string? pin)
    {
        if (LockedSeconds() is > 0 and var seconds)
        {
            throw new SetupException("pin_locked", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var expected = Convert.FromBase64String(file.Hash!);
        var actual = Hash(pin ?? "", Convert.FromBase64String(file.Salt ?? ""), file.Iterations);
        if (CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            _failures = 0;
            return;
        }

        if (++_failures >= MaxAttempts)
        {
            _failures = 0;
            _lockedUntil = _clock.GetUtcNow() + Lockout;
            throw new SetupException("pin_locked", ((int)Lockout.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        throw new SetupException("pin_wrong", (MaxAttempts - _failures).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private int LockedSeconds() =>
        _lockedUntil is { } until && until > _clock.GetUtcNow() ? (int)Math.Ceiling((until - _clock.GetUtcNow()).TotalSeconds) : 0;

    private AnalyticsUnlockResult Issue()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        _tokens[token] = _clock.GetUtcNow() + TokenLife;
        return new AnalyticsUnlockResult(token, (int)TokenLife.TotalSeconds);
    }

    /// <summary>Valid tokens slide: every use extends them by another 15 minutes.</summary>
    private bool Touch(string? token)
    {
        var now = _clock.GetUtcNow();
        foreach (var expired in _tokens.Where(t => t.Value <= now).Select(t => t.Key).ToList())
        {
            _tokens.Remove(expired);
        }

        if (string.IsNullOrEmpty(token) || !_tokens.ContainsKey(token))
        {
            return false;
        }

        _tokens[token] = now + TokenLife;
        return true;
    }

    private static byte[] Hash(string pin, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(pin, salt, Math.Max(iterations, 10_000), HashAlgorithmName.SHA256, 32);

    private AnalyticsFile Load()
    {
        if (path is null)
        {
            return _memory ?? new AnalyticsFile();
        }

        try
        {
            var info = new FileInfo(path);
            var file = info is { Exists: true, Length: < 4096 } ? JsonSerializer.Deserialize<AnalyticsFile>(File.ReadAllText(path)) : null;
            return Clean(file);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Unreadable = no PIN yet would let anyone in; keep the section locked instead.
            throw new SetupException("analytics_file", ex.Message, ex);
        }
    }

    private void Save(AnalyticsFile file)
    {
        if (path is null)
        {
            _memory = file;
            return;
        }

        var dir = Path.GetDirectoryName(path)!;
        var fresh = !Directory.Exists(dir);
        Directory.CreateDirectory(dir);
        if (fresh && OperatingSystem.IsWindows())
        {
            ServiceManager.RestrictFolder(dir);
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(file));
        File.Move(temp, path, overwrite: true);
    }

    private static AnalyticsFile Clean(AnalyticsFile? file)
    {
        file ??= new AnalyticsFile();
        var hash = file.Hash is { Length: > 0 } && file.Salt is { Length: > 0 } ? file.Hash : null;
        return file with
        {
            Hash = hash,
            Salt = hash is null ? null : file.Salt,
            Iterations = file.Iterations is >= 10_000 and <= 5_000_000 ? file.Iterations : Iterations,
            DayStartHour = file.DayStartHour is >= 0 and <= 23 ? file.DayStartHour : DefaultDayStartHour,
        };
    }

    private sealed record AnalyticsFile(string? Hash = null, string? Salt = null, int Iterations = Iterations, int DayStartHour = DefaultDayStartHour);
}
