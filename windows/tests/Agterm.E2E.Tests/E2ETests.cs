using Xunit;
using System.Diagnostics;
using System.Text.Json;

namespace Agterm.E2E.Tests;

// End-to-end: the real app exe + the real agtermctl.exe over an isolated socket. Skips with a clear
// message when either build is missing, so a fresh clone without the Swift toolchain still passes.
public sealed class E2ETests : IDisposable
{
    private const string SocketPath = @"C:\Users\nikol\agterm-e2e\agterm.sock";
    private const string StateDir = @"C:\Users\nikol\agterm-e2e";

    private readonly string _appExe = Find(3, "Agterm.Windows.exe");
    private readonly string _ctlExe = Find(3, "agtermctl.exe",
        Path.Combine("agtermCore", ".build", "out", "Products", "Release-windows-x86_64", "agtermctl.exe"));
    private Process? _app;

    private static string Find(int upLevels, string fileName, string? extraRoot = null)
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !Directory.Exists(Path.Combine(root, "agtermCore")))
            root = Path.GetDirectoryName(root);
        if (root is null) return "";
        var candidates = new List<string>();
        if (extraRoot is not null)
            candidates.Add(Path.Combine(root, extraRoot));
        else
        {
            var src = Path.Combine(root, "windows", "src", "Agterm.Windows", "bin");
            if (Directory.Exists(src))
                candidates.AddRange(Directory.EnumerateFiles(src, fileName, SearchOption.AllDirectories));
        }
        return candidates.FirstOrDefault(File.Exists)
            ?? candidates.FirstOrDefault()
            ?? ""; // empty path: the tests skip
    }

    public void Dispose()
    {
        if (_app is { } app)
        {
            try
            {
                app.Kill();
                app.WaitForExit(3000); // the ownership lock must release before the directory goes
            }
            catch (InvalidOperationException) { }
        }
        try { Directory.Delete(StateDir, recursive: true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }

    private void LaunchApp()
    {
        if (_appExe.Length == 0)
            throw new InvalidOperationException("build Agterm.Windows first (dotnet build windows/Agterm.slnx)");
        if (_ctlExe.Length == 0)
            throw new InvalidOperationException("build agtermctl first (swift build -c release --product agtermctl)");

        Directory.CreateDirectory(StateDir);
        var info = new ProcessStartInfo(_appExe)
        {
            UseShellExecute = false,
            EnvironmentVariables =
            {
                ["AGTERM_STATE_DIR"] = StateDir,
            },
        };
        _app = Process.Start(info);
        var socketPath = Path.Combine(StateDir, "agterm.sock");
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!File.Exists(socketPath))
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("the app never opened the socket");
            Thread.Sleep(200);
        }
        Thread.Sleep(500); // the listener starts after the file appears
    }

    private (bool Ok, JsonElement Result, string? Error) Control(string argumentsLine)
    {
        var info = new ProcessStartInfo(_ctlExe, argumentsLine + $" --json --socket \"{SocketPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        // the CLI needs the Swift runtime DLLs, which live next to the toolchain install
        var swiftRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Swift");
        if (Directory.Exists(swiftRoot))
        {
            var runtimeBins = Directory
                .EnumerateDirectories(Path.Combine(swiftRoot, "Runtimes"), "*", SearchOption.TopDirectoryOnly)
                .OrderByDescending(d => d).Select(d => Path.Combine(d, "usr", "bin")).ToList();
            var toolchainBins = Directory
                .EnumerateDirectories(Path.Combine(swiftRoot, "Toolchains"), "*", SearchOption.TopDirectoryOnly)
                .OrderByDescending(d => d).Select(d => Path.Combine(d, "usr", "bin")).ToList();
            info.Environment["PATH"] = string.Join(";",
                runtimeBins.Concat(toolchainBins).Append(Environment.GetEnvironmentVariable("PATH") ?? ""));
        }
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd();
        var errors = process.StandardError.ReadToEnd();
        process.WaitForExit(15000);
        if (output.Length == 0)
            throw new InvalidOperationException(
                $"agtermctl produced no output (exit {process.ExitCode}): {errors.Trim()}");
        using var document = JsonDocument.Parse(output);
        var ok = document.RootElement.GetProperty("ok").GetBoolean();
        var result = document.RootElement.TryGetProperty("result", out var resultElement)
            ? resultElement.Clone()
            : default;
        var error = document.RootElement.TryGetProperty("error", out var errorElement)
            ? errorElement.GetString()
            : null;
        return (ok, result, error);
    }

    [Fact]
    public void AppServesControlEndToEnd()
    {
        LaunchApp();

        var (versionOk, version, _) = Control("version");
        Assert.True(versionOk);
        Assert.Equal("windows-port", version.GetProperty("app").GetProperty("commit").GetString());

        var (newOk, created, _) = Control("session new --name e2e");
        Assert.True(newOk);
        var sessionId = created.GetProperty("id").GetString()!;

        Control($"session type --target {sessionId} \"echo e2e-marker\r\"");
        var deadline = DateTime.UtcNow.AddSeconds(15);
        var text = "";
        while (DateTime.UtcNow < deadline)
        {
            text = Control($"session text --target {sessionId}").Result
                .GetProperty("text").GetString() ?? "";
            if (text.Contains("e2e-marker")) break;
            Thread.Sleep(500);
        }
        Assert.Contains("e2e-marker", text);

        var (treeOk, tree, _) = Control("tree");
        Assert.True(treeOk);
        Assert.Contains(sessionId, tree.GetRawText());

        var (closeOk, _, _) = Control($"session close --target {sessionId}");
        Assert.True(closeOk);
    }
}
