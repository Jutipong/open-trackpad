using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using OpenTrackpad.Interop.Driver;

namespace OpenTrackpad.App;

public static class ElevatedSettingsApplier
{
    public const string ApplyArgument = "--apply-settings";

    public static void Apply(DriverSettings settings)
    {
        string directory = Path.Combine(Path.GetTempPath(), "OpenTrackpad");
        Directory.CreateDirectory(directory);

        string settingsPath = Path.Combine(directory, $"apply-{Guid.NewGuid():N}.json");
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings));

        try
        {
            var startInfo = BuildStartInfo(settingsPath);
            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("เปิดตัวช่วย elevated ไม่สำเร็จ");

            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("ตัวช่วย elevated ไม่ตอบสนองภายใน 30 วินาที");
            }

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"ตัวช่วย elevated จบด้วยรหัส {process.ExitCode}");
        }
        finally
        {
            try { File.Delete(settingsPath); } catch { }
        }
    }

    private static ProcessStartInfo BuildStartInfo(string settingsPath)
    {
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
            throw new InvalidOperationException("ไม่พบ path ของโปรแกรม");

        bool isDotnet = Path.GetFileName(exe).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase);
        string arguments;
        if (isDotnet)
        {
            string? dll = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(dll))
                throw new InvalidOperationException("ไม่พบ path ของ OpenTrackpad.App.dll");
            arguments = $"\"{dll}\" {ApplyArgument} \"{settingsPath}\"";
        }
        else
        {
            arguments = $"{ApplyArgument} \"{settingsPath}\"";
        }

        return new ProcessStartInfo
        {
            FileName = exe,
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
        };
    }

    public static bool TryApplyFromArguments(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length < 2 || !string.Equals(args[0], ApplyArgument, StringComparison.Ordinal))
            return false;

        try
        {
            string json = File.ReadAllText(args[1]);
            DriverSettings settings = JsonSerializer.Deserialize<DriverSettings>(json)
                ?? throw new InvalidDataException("settings JSON ว่าง");

            DriverSettingsStore.Write(settings);

            ControlPanelResponse response = new NamedPipeControlPanelPipe().ReloadSettings();
            if (!response.Ok)
            {
                exitCode = 2;
                return true;
            }
        }
        catch (Exception ex)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Root);
                File.AppendAllText(
                    Path.Combine(AppPaths.Root, "elevated.log"),
                    $"[{DateTimeOffset.Now:O}] {ex}\n\n");
            }
            catch
            {
                // Best effort only.
            }

            exitCode = 3;
            return true;
        }

        return true;
    }
}
