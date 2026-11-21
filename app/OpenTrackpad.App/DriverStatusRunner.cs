using System.Diagnostics;
using System.IO;

namespace OpenTrackpad.App;

public static class ScriptLocator
{
    public static string? Find(params string[] relativePaths)
    {
        foreach (string root in CandidateRoots())
        {
            foreach (string relativePath in relativePaths)
            {
                string path = Path.Combine(
                    root, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path)) return path;
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateRoots()
    {
        yield return AppContext.BaseDirectory;

        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        for (int depth = 0; depth < 8 && directory?.Parent is not null; depth++)
        {
            directory = directory.Parent;
            yield return directory.FullName;
        }
    }
}

public static class DriverStatusRunner
{
    public static string RunStatus(bool json = false)
    {
        string script = ScriptLocator.Find(
            "scripts/Test-DriverStatus.ps1",
            "installer/scripts/Test-DriverStatus.ps1")
            ?? throw new FileNotFoundException("Test-DriverStatus.ps1 was not found.");

        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(script);
        if (json) start.ArgumentList.Add("-Json");

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("Driver status process did not start.");

        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("Driver status timed out.");
        }

        return $"{output.Result}\n{error.Result}".Trim();
    }

    /// <summary>Runs the shipped enable-user-access.ps1 elevated and returns its exit code.</summary>
    public static int RunElevatedAccessFix()
    {
        string script = ScriptLocator.Find("scripts/enable-user-access.ps1")
            ?? throw new FileNotFoundException("enable-user-access.ps1 was not found.");

        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"",
        };

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("The elevated helper did not start.");

        if (!process.WaitForExit(60_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("The elevated helper did not respond within 60 seconds.");
        }

        return process.ExitCode;
    }
}
