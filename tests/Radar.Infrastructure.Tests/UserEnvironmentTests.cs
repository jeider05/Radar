using System.Runtime.InteropServices;
using Radar.Infrastructure.Gemini;

namespace Radar.Infrastructure.Tests;

public sealed class UserEnvironmentTests
{
    [Fact]
    public void DetectOs_uses_the_override_variable_when_set()
    {
        var os = UserEnvironment.DetectOs(name => name == UserEnvironment.OsVariable ? "Custom OS" : null);

        Assert.Equal("Custom OS", os);
    }

    [Fact]
    public void DetectOs_without_override_reports_the_current_platform()
    {
        var os = UserEnvironment.DetectOs(_ => null);

        var expected = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macOS"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Linux"
            : RuntimeInformation.OSDescription;
        Assert.Equal(expected, os);
    }

    [Fact]
    public void DetectShell_uses_the_override_variable_when_set()
    {
        var shell = UserEnvironment.DetectShell(name => name == UserEnvironment.ShellVariable ? "fish" : null);

        Assert.Equal("fish", shell);
    }

    [Fact]
    public void DetectShell_without_override_falls_back_to_SHELL_on_non_windows()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return; // Windows always reports PowerShell, regardless of SHELL.
        }

        var shell = UserEnvironment.DetectShell(name => name == "SHELL" ? "/bin/zsh" : null);

        Assert.Equal("zsh", shell);
    }
}
