using System.Runtime.InteropServices;

namespace Radar.Infrastructure.Gemini;

/// <summary>Detects the current user's OS and shell for the release-detail prompt's installation commands.</summary>
public static class UserEnvironment
{
    public const string OsVariable = "RADAR_USER_OS";
    public const string ShellVariable = "RADAR_USER_SHELL";

    /// <summary>"Windows", "macOS" or "Linux", unless overridden by <see cref="OsVariable"/>.</summary>
    public static string DetectOs(Func<string, string?>? getVariable = null)
    {
        getVariable ??= Environment.GetEnvironmentVariable;
        if (getVariable(OsVariable) is { Length: > 0 } overridden)
        {
            return overridden.Trim();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "Windows";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "macOS";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "Linux";
        }

        return RuntimeInformation.OSDescription;
    }

    /// <summary>The user's shell name, unless overridden by <see cref="ShellVariable"/>.</summary>
    public static string DetectShell(Func<string, string?>? getVariable = null)
    {
        getVariable ??= Environment.GetEnvironmentVariable;
        if (getVariable(ShellVariable) is { Length: > 0 } overridden)
        {
            return overridden.Trim();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "PowerShell";
        }

        if (getVariable("SHELL") is { Length: > 0 } shellPath)
        {
            return Path.GetFileName(shellPath);
        }

        return "shell";
    }
}
