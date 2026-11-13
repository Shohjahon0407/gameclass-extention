using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Manajoy.GameClass.Bridge.Hosting;
using Microsoft.Extensions.Options;

namespace Manajoy.GameClass.Bridge.Configuration;

/// <summary>
/// Fills SqlLogin.Password and Push.Key from their DPAPI-protected files (written by install-service.ps1)
/// when they were not configured directly.
/// </summary>
internal sealed class SqlPasswordFile : IPostConfigureOptions<BridgeOptions>
{
    public void PostConfigure(string? name, BridgeOptions options)
    {
        var login = options.SqlLogin;
        if (login.IsConfigured && string.IsNullOrEmpty(login.Password))
        {
            login.Password = ReadProtected(login.PasswordFile);
        }

        var push = options.Push;
        if (push.IsEnabled && string.IsNullOrEmpty(push.Key))
        {
            push.Key = ReadProtected(push.KeyFile);
        }
    }

    private static string? ReadProtected(string? file)
    {
        if (string.IsNullOrWhiteSpace(file) || !OperatingSystem.IsWindows())
        {
            return null;
        }

        var path = BridgePaths.Resolve(file);
        return File.Exists(path) ? Unprotect(File.ReadAllBytes(path)) : null;
    }

    [SupportedOSPlatform("windows")]
    public static byte[] Protect(string password) =>
        ProtectedData.Protect(Encoding.UTF8.GetBytes(password), optionalEntropy: null, DataProtectionScope.LocalMachine);

    [SupportedOSPlatform("windows")]
    public static string Unprotect(byte[] data) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(data, optionalEntropy: null, DataProtectionScope.LocalMachine));
}
