using System.Text;
using Microsoft.Win32;

namespace Agterm.Terminal;

/// <summary>A launchable shell: display name plus its ConPTY command line.</summary>
public sealed record ShellProfile(string Name, string CommandLine)
{
    public override string ToString() => Name;
}

/// <summary>
/// Detects the standard Windows shells — PowerShell (pwsh or powershell), cmd, Git Bash, and one profile
/// per WSL distro. Detection is tolerant: anything absent is simply not offered.
/// </summary>
public static class ShellProfiles
{
    public static List<ShellProfile> Detect()
    {
        var profiles = new List<ShellProfile>();
        var powerShell = PowerShellPath();
        if (powerShell is not null)
            profiles.Add(new ShellProfile("PowerShell", $"\"{powerShell}\" -NoLogo"));
        if (Environment.GetEnvironmentVariable("ComSpec") is { } comSpec)
            profiles.Add(new ShellProfile("Command Prompt", $"\"{comSpec}\""));
        var gitBash = GitBashPath();
        if (gitBash is not null)
            profiles.Add(new ShellProfile("Git Bash", $"\"{gitBash}\" -i -l"));
        foreach (var distro in WslDistros())
            profiles.Add(new ShellProfile($"WSL: {distro}", $"wsl.exe -d {distro}"));
        if (profiles.Count == 0)
            profiles.Add(new ShellProfile("Command Prompt", "cmd.exe"));
        return profiles;
    }

    public static ShellProfile Default() => Detect().First();

    private static string? PowerShellPath()
    {
        var pwsh = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "PowerShell", "7", "pwsh.exe");
        if (File.Exists(pwsh)) return pwsh;
        var which = ProbePath("pwsh.exe");
        if (which is not null) return which;
        return ProbePath("powershell.exe");
    }

    private static string? GitBashPath()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\GitForWindows");
            if (key?.GetValue("InstallPath") is string installPath)
            {
                var bash = Path.Combine(installPath, "bin", "bash.exe");
                if (File.Exists(bash)) return bash;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // registry unreadable — fall through to the fixed probe
        }
        var fixedPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe");
        return File.Exists(fixedPath) ? fixedPath : null;
    }

    /// <summary>wsl.exe prints UTF-16LE even into a pipe — read it that way or the names come out mojibake.</summary>
    private static List<string> WslDistros()
    {
        if (ProbePath("wsl.exe") is null) return [];
        try
        {
            var info = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = "-l -q",
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.Unicode,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = System.Diagnostics.Process.Start(info);
            if (process is null) return [];
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => !line.Contains("docker-desktop", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return [];
        }
    }

    private static string? ProbePath(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        char[] separators = OperatingSystem.IsWindows() ? [';'] : [':'];
        foreach (var directory in path.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), executable);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // malformed PATH segment — skip it
            }
        }
        return null;
    }
}
