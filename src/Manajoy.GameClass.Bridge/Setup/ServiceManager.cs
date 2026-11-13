using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Text.Json;
using Manajoy.GameClass.Bridge.Configuration;
using Microsoft.Extensions.Logging;

namespace Manajoy.GameClass.Bridge.Setup;

/// <summary>
/// Installs / updates / removes the bridge Windows Service (C# twin of install-service.ps1):
/// copy the exe to Program Files, restrict the folder to Administrators + SYSTEM, store secrets with
/// DPAPI (LocalMachine), register the service (delayed auto start, restart on failure) and start it.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ServiceManager(SetupEnvironment environment, LocalBridgeClient localBridge, ILogger<ServiceManager> logger)
{
    private static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan PushTimeout = TimeSpan.FromSeconds(45);

    public string InstallDir => environment.ResolveInstallDir();

    public string InstalledExe => Path.Combine(InstallDir, SetupEnvironment.ExeName);

    public static bool IsInstalled => SetupEnvironment.ServiceExePath() is not null;

    /// <summary>The version of the exe the service runs (from its file), or null.</summary>
    public Version? InstalledVersion()
    {
        var exe = SetupEnvironment.ServiceExePath();
        if (exe is null || !File.Exists(exe))
        {
            return null;
        }

        var text = FileVersionInfo.GetVersionInfo(exe).ProductVersion ?? "";
        return Version.TryParse(text.Split('+')[0], out var version) ? version : null;
    }

    public static string? ServiceStatus()
    {
        try
        {
            using var service = new ServiceController(SetupEnvironment.ServiceName);
            return service.Status.ToString();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public bool RunningFromInstallDir =>
        string.Equals(Path.GetDirectoryName(SetupEnvironment.CurrentExePath)?.TrimEnd('\\'), InstallDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    /// <summary>Full install: new exe, new read-only login secret and service settings.</summary>
    public Task<InstallResult> InstallAsync(string sqlUser, string sqlPassword, string? server, string? database, CancellationToken cancellationToken) =>
        RunInstallAsync(new Credentials(sqlUser, sqlPassword, server, database), cancellationToken);

    /// <summary>Update to this exe's version: the login, secrets and Manajoy pairing stay as they are.</summary>
    public Task<InstallResult> UpdateAsync(CancellationToken cancellationToken) => RunInstallAsync(null, cancellationToken);

    private sealed record Credentials(string SqlUser, string SqlPassword, string? Server, string? Database);

    private async Task<InstallResult> RunInstallAsync(Credentials? credentials, CancellationToken cancellationToken)
    {
        var steps = new List<CheckResult>();
        var dir = InstallDir;

        await RunStepAsync(steps, "stop_old", async () =>
        {
            if (!IsInstalled)
            {
                return "new";
            }

            await StopAsync(cancellationToken);
            return "stopped";
        });

        await RunStepAsync(steps, "copy_files", () =>
        {
            Directory.CreateDirectory(dir);
            RestrictFolder(dir);
            var copied = CopySelf(dir);
            var settings = Path.Combine(dir, SetupEnvironment.AppSettingsFile);
            if (!File.Exists(settings))
            {
                // A Telegram download is a single exe: write the default settings next to the installed copy.
                File.WriteAllText(settings, DefaultAppSettings());
            }

            RestrictFolder(dir);
            AllowUsersToStart(Path.Combine(dir, SetupEnvironment.ExeName));
            return Task.FromResult(copied ? dir : $"{dir} (already in place)");
        });

        if (credentials is not null)
        {
            await RunStepAsync(steps, "store_secret", () =>
            {
                File.WriteAllBytes(Path.Combine(dir, environment.SqlSecretFile), SqlPasswordFile.Protect(credentials.SqlPassword));
                return Task.FromResult("DPAPI");
            });
        }

        await RunStepAsync(steps, "register_service", () =>
        {
            RegisterService(Path.Combine(dir, SetupEnvironment.ExeName));
            if (credentials is not null)
            {
                var env = SetupEnvironment.ReadServiceEnvironment();
                env[SetupEnvironment.EnvSqlUser] = credentials.SqlUser;
                SetOrRemove(env, SetupEnvironment.EnvServer, credentials.Server, SetupEnvironment.DefaultServer);
                SetOrRemove(env, SetupEnvironment.EnvDatabase, credentials.Database, SetupEnvironment.DefaultDatabase);
                SetupEnvironment.WriteServiceEnvironment(env);
            }

            return Task.FromResult(SetupEnvironment.ServiceName);
        });

        await RunStepAsync(steps, "start_service", async () =>
        {
            await StartAsync(cancellationToken);
            return "Running";
        });

        LocalSummary? local = null;
        await RunStepAsync(steps, "read_gameclass", async () =>
        {
            local = await WaitForHealthAsync(cancellationToken);
            return $"{local.Pcs.Count} PC";
        });

        if (steps.All(step => step.Level != CheckLevel.Fail))
        {
            // The Windows installer already made its own Start menu / desktop shortcuts.
            steps.Add(SetupEnvironment.RunningFromInstaller()
                ? new CheckResult("shortcut", CheckLevel.Ok, "installer")
                : TryCreateShortcut(Path.Combine(dir, SetupEnvironment.ExeName)));
        }

        return new InstallResult(steps.All(s => s.Level != CheckLevel.Fail), steps, local);
    }

    public async Task<PushInfo> ConfigurePushAsync(string pushUrl, string pushKey, int sellerId, string clubName, CancellationToken cancellationToken)
    {
        var dir = InstallDir;
        File.WriteAllBytes(Path.Combine(dir, environment.PushSecretFile), SqlPasswordFile.Protect(pushKey));
        File.WriteAllText(Path.Combine(dir, SetupEnvironment.ClubInfoFile), JsonSerializer.Serialize(new ClubInfoFile(sellerId, clubName, DateTimeOffset.Now)));
        var env = SetupEnvironment.ReadServiceEnvironment();
        env[SetupEnvironment.EnvPushUrl] = pushUrl;
        SetupEnvironment.WriteServiceEnvironment(env);

        var pairedAt = DateTimeOffset.Now;
        await RestartAsync(cancellationToken);
        return await WaitForPushAsync(pairedAt, cancellationToken);
    }

    public async Task<StepsResult> DisablePushAsync(CancellationToken cancellationToken)
    {
        var steps = new List<CheckResult>();
        await RunStepAsync(steps, "push_disabled", async () =>
        {
            var env = SetupEnvironment.ReadServiceEnvironment();
            env.Remove(SetupEnvironment.EnvPushUrl);
            SetupEnvironment.WriteServiceEnvironment(env);
            DeleteIfExists(Path.Combine(InstallDir, environment.PushSecretFile));
            DeleteIfExists(Path.Combine(InstallDir, SetupEnvironment.ClubInfoFile));
            await RestartAsync(cancellationToken);
            return string.Empty;
        });
        return new StepsResult(steps.All(s => s.Level == CheckLevel.Ok), steps);
    }

    /// <summary>Daily reports to Manajoy: absent = on (the default), "false" = off. The service reads it at start.</summary>
    public static bool ReportsPushEnabled() =>
        !SetupEnvironment.ReadServiceEnvironment().TryGetValue(SetupEnvironment.EnvAnalyticsEnabled, out var value)
        || !value.Equals("false", StringComparison.OrdinalIgnoreCase);

    public async Task SetReportsPushAsync(bool enabled, CancellationToken cancellationToken)
    {
        var env = SetupEnvironment.ReadServiceEnvironment();
        if (enabled)
        {
            env.Remove(SetupEnvironment.EnvAnalyticsEnabled);
        }
        else
        {
            env[SetupEnvironment.EnvAnalyticsEnabled] = "false";
        }

        SetupEnvironment.WriteServiceEnvironment(env);
        await RestartAsync(cancellationToken);
    }

    public async Task<StepsResult> RestartServiceAsync(CancellationToken cancellationToken)
    {
        var steps = new List<CheckResult>();
        await RunStepAsync(steps, "service_restarted", async () =>
        {
            await RestartAsync(cancellationToken);
            var local = await WaitForHealthAsync(cancellationToken);
            return local.Status;
        });
        return new StepsResult(steps.All(s => s.Level == CheckLevel.Ok), steps);
    }

    public async Task<StepsResult> UninstallAsync(UninstallRequest request, CancellationToken cancellationToken)
    {
        var steps = new List<CheckResult>();
        var dir = InstallDir;

        await RunStepAsync(steps, "service_removed", async () =>
        {
            if (IsInstalled)
            {
                await StopAsync(cancellationToken);
                RunTool("sc.exe", "delete", SetupEnvironment.ServiceName);
            }

            return SetupEnvironment.ServiceName;
        });

        if (request.DropLogin)
        {
            await RunStepAsync(steps, "login_dropped", () =>
            {
                ReadOnlyLoginProvisioner.Drop(request, SetupEnvironment.ReadOnlyLogin);
                return Task.FromResult(SetupEnvironment.ReadOnlyLogin);
            });
        }

        await RunStepAsync(steps, "files_removed", () =>
        {
            foreach (var path in ShortcutPaths())
            {
                DeleteIfExists(path);
            }

            try
            {
                SetupCopy.Remove();
                AutoStart.Disable();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SetupException)
            {
                logger.LogWarning("Setup copy / sign-in task not removed: {Message}", ex.Message);
            }

            if (!Directory.Exists(dir))
            {
                return Task.FromResult(dir);
            }

            var uninstaller = Path.Combine(dir, SetupEnvironment.InstallerUninstallExe);
            if (File.Exists(uninstaller))
            {
                // Installed by the Windows installer: its uninstaller removes the files, shortcuts and the
                // "Apps & features" entry, a few seconds after this window has closed.
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 6 127.0.0.1 >nul & \"{uninstaller}\" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                });
                return Task.FromResult($"{dir} (Windows uninstaller)");
            }

            if (RunningFromInstallDir)
            {
                // This exe is in use: delete the folder a few seconds after the window closes.
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 6 127.0.0.1 >nul & rmdir /s /q \"{dir}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                });
                return Task.FromResult($"{dir} (after closing)");
            }

            Directory.Delete(dir, recursive: true);
            var parent = Path.GetDirectoryName(dir);
            if (parent is not null && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
            {
                Directory.Delete(parent);
            }

            return Task.FromResult(dir);
        });

        return new StepsResult(steps.All(s => s.Level == CheckLevel.Ok), steps);
    }

    public ClubInfoFile? ReadClubInfo()
    {
        try
        {
            var path = Path.Combine(InstallDir, SetupEnvironment.ClubInfoFile);
            return File.Exists(path) ? JsonSerializer.Deserialize<ClubInfoFile>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public string? ReadPushKey()
    {
        var path = Path.Combine(InstallDir, environment.PushSecretFile);
        return File.Exists(path) ? SqlPasswordFile.Unprotect(File.ReadAllBytes(path)) : null;
    }

    public bool SqlSecretExists => File.Exists(Path.Combine(InstallDir, environment.SqlSecretFile));

    public sealed record ClubInfoFile(int SellerId, string ClubName, DateTimeOffset PairedAt);

    private async Task RunStepAsync(List<CheckResult> steps, string id, Func<Task<string>> action)
    {
        if (steps.Any(step => step.Level == CheckLevel.Fail))
        {
            return;
        }

        try
        {
            var detail = await action();
            steps.Add(new CheckResult(id, CheckLevel.Ok, detail));
            logger.LogInformation("Setup step {Step}: ok {Detail}", id, detail);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var detail = ex is SetupException setup ? $"{setup.Code}: {setup.Detail}" : ex.Message;
            steps.Add(new CheckResult(id, CheckLevel.Fail, detail));
            logger.LogError(ex, "Setup step {Step} failed", id);
        }
    }

    private static void SetOrRemove(Dictionary<string, string> env, string name, string? value, string defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Equals(defaultValue, StringComparison.OrdinalIgnoreCase))
        {
            env.Remove(name);
        }
        else
        {
            env[name] = value.Trim();
        }
    }

    private bool CopySelf(string dir)
    {
        var source = SetupEnvironment.CurrentExePath;
        var target = Path.Combine(dir, SetupEnvironment.ExeName);
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Copy next to the target first, check it byte for byte (SHA-256), then swap: a half-written or
        // misread exe never replaces a working one.
        var staging = target + ".new";
        File.Copy(source, staging, overwrite: true);
        var expected = SetupCopy.HashFile(source, out _);
        var written = SetupCopy.HashFile(staging, out _);
        if (written != expected)
        {
            File.Delete(staging);
            throw new SetupException("copy_verify_failed", $"{staging}: sha256 {written[..12]}… ≠ {expected[..12]}…");
        }

        File.Move(staging, target, overwrite: true);
        return true;
    }

    /// <summary>Folder owned by Administrators, only Administrators and SYSTEM have access; children inherit.</summary>
    internal static void RestrictFolder(string dir)
    {
        RunTool("icacls.exe", dir, "/setowner", "*S-1-5-32-544", "/T", "/C", "/Q");
        RunTool("icacls.exe", dir, "/inheritance:r", "/grant:r", "*S-1-5-32-544:(OI)(CI)F", "*S-1-5-18:(OI)(CI)F", "/C", "/Q");
        if (Directory.EnumerateFileSystemEntries(dir).Any())
        {
            RunTool("icacls.exe", Path.Combine(dir, "*"), "/reset", "/T", "/C", "/Q");
        }
    }

    /// <summary>
    /// The desktop shortcut is opened by the non-elevated explorer, which must be able to read the exe to show
    /// the UAC prompt. Only the exe is opened up; the *.secret files keep Administrators/SYSTEM-only access.
    /// </summary>
    internal static void AllowUsersToStart(string exePath) =>
        RunTool("icacls.exe", exePath, "/grant", "*S-1-5-32-545:(RX)", "/C", "/Q");

    private static void RegisterService(string exePath)
    {
        var binPath = $"\"{exePath}\"";
        if (IsInstalled)
        {
            RunTool("sc.exe", "config", SetupEnvironment.ServiceName, "binPath=", binPath, "start=", "delayed-auto",
                "DisplayName=", SetupEnvironment.ServiceDisplayName);
        }
        else
        {
            RunTool("sc.exe", "create", SetupEnvironment.ServiceName, "binPath=", binPath, "start=", "delayed-auto",
                "DisplayName=", SetupEnvironment.ServiceDisplayName);
        }

        RunTool("sc.exe", "description", SetupEnvironment.ServiceName, SetupEnvironment.ServiceDescription);
        RunTool("sc.exe", "failure", SetupEnvironment.ServiceName, "reset=", "86400", "actions=", "restart/10000/restart/30000/restart/60000");
    }

    private static async Task StartAsync(CancellationToken cancellationToken)
    {
        using var service = new ServiceController(SetupEnvironment.ServiceName);
        if (service.Status != ServiceControllerStatus.Running)
        {
            if (service.Status != ServiceControllerStatus.StartPending)
            {
                service.Start();
            }

            await Task.Run(() => service.WaitForStatus(ServiceControllerStatus.Running, ServiceTimeout), cancellationToken);
        }
    }

    /// <summary>
    /// <c>--uninstall-service</c>, run by the Windows uninstaller before it deletes the files: stop and delete
    /// the service and the setup copy. The read-only SQL login stays (dropping it needs the sa password; the
    /// window's "O'chirib tashlash" offers that). Exit code 0 also when there is nothing to remove.
    /// </summary>
    public static int RemoveForUninstaller()
    {
        var failed = false;
        void Try(Action step)
        {
            try
            {
                step();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                failed = true;
            }
        }

        if (IsInstalled)
        {
            // An open setup window also counts as "still running"; the service is deleted regardless.
            Try(() => StopAsync(CancellationToken.None).GetAwaiter().GetResult());
            Try(() => RunTool("sc.exe", "delete", SetupEnvironment.ServiceName));
        }

        Try(() => SetupCopy.Remove());
        Try(AutoStart.Disable);
        return failed ? 1 : 0;
    }

    private static async Task StopAsync(CancellationToken cancellationToken)
    {
        using (var service = new ServiceController(SetupEnvironment.ServiceName))
        {
            if (service.Status is not (ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending))
            {
                service.Stop();
            }

            await Task.Run(() => service.WaitForStatus(ServiceControllerStatus.Stopped, ServiceTimeout), cancellationToken);
        }

        // The process may linger briefly after SCM reports Stopped; the exe cannot be replaced until it exits.
        var self = Environment.ProcessId;
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(SetupEnvironment.ExeName)))
        {
            using (process)
            {
                if (process.Id != self && !process.WaitForExit((int)ServiceTimeout.TotalMilliseconds))
                {
                    throw new SetupException("service_stop_timeout", $"PID {process.Id}");
                }
            }
        }
    }

    private static async Task RestartAsync(CancellationToken cancellationToken)
    {
        await StopAsync(cancellationToken);
        await StartAsync(cancellationToken);
    }

    private async Task<LocalSummary> WaitForHealthAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + HealthTimeout;
        LocalSummary? last = null;
        while (DateTime.UtcNow < deadline)
        {
            var reading = await localBridge.ReadAsync(includePcs: true, cancellationToken);
            last = reading.Local;
            if (last.Status is "ok" or "degraded")
            {
                return last;
            }

            if (last.Status == "unavailable" && last.LastError is not null)
            {
                throw new SetupException("gameclass_read_failed", last.LastError);
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        throw new SetupException("health_timeout", last?.LastError ?? last?.Status);
    }

    private async Task<PushInfo> WaitForPushAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + PushTimeout;
        PushInfo? last = null;
        while (DateTime.UtcNow < deadline)
        {
            var reading = await localBridge.ReadAsync(includePcs: false, cancellationToken);
            last = reading.Push;
            if (last is { LastSuccessAt: { } success } && success >= since.AddSeconds(-2))
            {
                return last;
            }

            if (last is { LastError: not null, ConsecutiveFailures: > 0 } && last.LastAttemptAt >= since)
            {
                return last;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        return last ?? new PushInfo(true, null, null, null, "timeout", 0);
    }

    private CheckResult TryCreateShortcut(string exePath)
    {
        try
        {
            // Desktop and Start menu: from Start the window opens without "show desktop", which would minimise
            // GCServer into the notification area (it hides there on minimise) and look like it closed.
            CreateShortcuts(exePath);
            return new CheckResult("shortcut", CheckLevel.Ok, string.Join(" + ", ShortcutPaths()));
        }
        catch (Exception ex)
        {
            logger.LogWarning("Desktop shortcut not created: {Message}", ex.Message);
            return new CheckResult("shortcut", CheckLevel.Warn, ex.Message);
        }
    }

    /// <summary>Desktop and Start menu "Manajoy GameClass" shortcuts to <paramref name="exePath"/> (replaced if present).</summary>
    internal static void CreateShortcuts(string exePath)
    {
        foreach (var path in ShortcutPaths())
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            ShellShortcut.Create(path, exePath, workingDirectory: Path.GetDirectoryName(exePath),
                description: "Manajoy GameClass", iconLocation: exePath, iconIndex: 0);
        }
    }

    private static string[] ShortcutPaths() =>
    [
        ShortcutPath(),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), SetupEnvironment.ShortcutName),
    ];

    private static string ShortcutPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), SetupEnvironment.ShortcutName);

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string DefaultAppSettings()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("appsettings.default.json")
            ?? throw new InvalidOperationException("appsettings.default.json resource missing");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Runs a Windows tool (sc.exe, icacls.exe) with literal arguments; a non-zero exit code is an error.</summary>
    internal static void RunTool(string tool, params string[] arguments)
    {
        var info = new ProcessStartInfo(tool)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new SetupException("tool_failed", tool);
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new SetupException("tool_failed", $"{tool} {arguments.FirstOrDefault()}: exit {process.ExitCode} {output.Trim()}");
        }
    }
}
