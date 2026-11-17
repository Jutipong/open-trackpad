using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using OpenTrackpad.Interop.Driver;
using OpenTrackpad.Interop.RawInput;

namespace OpenTrackpad.App;

public static class DiagnosticsExporter
{
    public static string Export(AppHost host)
    {
        Directory.CreateDirectory(AppPaths.Root);
        string path = Path.Combine(
            AppPaths.Root,
            $"OpenTrackpad-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss-fff}.zip");

        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);

        AddFile(archive, AppLog.FilePath, "app.log");
        for (int index = 1; index <= 3; index++)
            AddFile(archive, $"{AppLog.FilePath}.{index}", $"app.log.{index}");

        AddFile(archive, AppPaths.SettingsFile, "settings.json");
        AddText(archive, "system.txt", BuildSystemInfo(host));

        string? driverStatus = TryRunDriverStatus();
        if (!string.IsNullOrWhiteSpace(driverStatus))
            AddText(archive, "driver-status.json", driverStatus);

        return path;
    }

    private static string BuildSystemInfo(AppHost host)
    {
        var text = new StringBuilder();
        text.AppendLine($"OpenTrackpad v{AppVersion.Display}");
        text.AppendLine($"Exported: {DateTimeOffset.Now:O}");
        text.AppendLine($"OS: {Environment.OSVersion.VersionString} (build {Environment.OSVersion.Version.Build})");
        text.AppendLine($"Architecture: {RuntimeInformation.OSArchitecture}");
        text.AppendLine($"Admin: {host.IsAdministrator}");
        text.AppendLine($"Language: {Loc.Language}");

        IReadOnlyList<TrackpadDevice> devices = TrackpadDeviceLocator.FindAll();
        text.AppendLine($"Transport: {devices.FirstOrDefault()?.Transport ?? TrackpadTransport.None}");
        text.AppendLine($"Device count: {devices.Count}");

        foreach (TrackpadDevice device in devices)
            text.AppendLine($"- transport={device.Transport} instance={Redact(device.InstanceId)}");

        text.AppendLine(
            $"Settings: drag={host.Settings.DragEnabled}, gain={host.GetDragGain():0.00}, " +
            $"gestureBackup={host.HasGestureBackup}");

        try
        {
            DriverSettings driver = DriverSettingsStore.Read();
            text.AppendLine(
                $"Driver: click=0x{driver.FeedbackClick:X6}, release=0x{driver.FeedbackRelease:X6}, " +
                $"stopPressure={driver.StopPressure}, stopSize={driver.StopSize}, " +
                $"palm={driver.PalmRejection}, near={driver.IgnoreNearFingers}, " +
                $"buttonFinger={driver.IgnoreButtonFinger}, invert={driver.InvertMovement}, " +
                $"buttonDisabled={driver.ButtonDisabled}");
        }
        catch (Exception ex)
        {
            text.AppendLine($"Driver settings: unavailable ({ex.Message})");
        }

        try
        {
            text.AppendLine($"Cursor speed: {host.GetCursorSpeed()}");
        }
        catch (Exception ex)
        {
            text.AppendLine($"Cursor speed: unavailable ({ex.Message})");
        }

        text.AppendLine($"Three-finger preset: {host.GetThreeFingerPreset()?.ToString() ?? "custom"}");
        text.AppendLine($"Four-finger preset: {host.GetFourFingerPreset()?.ToString() ?? "custom"}");
        return text.ToString();
    }

    private static string? TryRunDriverStatus()
    {
        string script = Path.Combine(AppContext.BaseDirectory, "scripts", "Test-DriverStatus.ps1");
        if (!File.Exists(script)) return null;

        try
        {
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
            start.ArgumentList.Add("-Json");

            using Process? process = Process.Start(start);
            if (process is null) return "Driver status process did not start.";

            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return "Driver status timed out.";
            }

            return $"{output.Result}\n{error.Result}".Trim();
        }
        catch (Exception ex)
        {
            return "Driver status failed: " + ex.Message;
        }
    }

    private static void AddFile(ZipArchive archive, string path, string entryName)
    {
        if (!File.Exists(path)) return;

        try
        {
            using var source = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using Stream target = entry.Open();
            source.CopyTo(target);
        }
        catch (Exception ex)
        {
            AddText(archive, entryName + ".error.txt", ex.ToString());
        }
    }

    private static void AddText(ZipArchive archive, string entryName, string text)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(text);
    }

    private static string Redact(string value) =>
        value.Length <= 12 ? "***" : $"{value[..8]}...{value[^4..]}";
}
