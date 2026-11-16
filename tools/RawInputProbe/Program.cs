using System.Text;
using System.Text.Json;
using RawInputProbe;
using RawInputProbe.Drag;
using RawInputProbe.Driver;
using RawInputProbe.Hid;
using RawInputProbe.Input;
using RawInputProbe.Native;
using RawInputProbe.Ptp;
using RawInputProbe.RawInput;

return ProbeApp.Run(args);

internal static class ProbeApp
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        try
        {
            switch (args[0].ToLowerInvariant())
            {
                case "caps":
                    return Caps(args.Skip(1).ToArray());
                case "rawlist":
                    return RawList();
                case "descriptor":
                    return Descriptor(args.Skip(1).ToArray());
                case "raw":
                    return Raw(args.Skip(1).ToArray());
                case "drag":
                    return Drag(args.Skip(1).ToArray());
                case "setfeature":
                    return SetFeature(args.Skip(1).ToArray());
                case "getfeature":
                    return GetFeature(args.Skip(1).ToArray());
                case "output":
                    return Output(args.Skip(1).ToArray());
                case "battery":
                    return Battery();
                case "haptic":
                    return Haptic(args.Skip(1).ToArray());
                case "reload":
                    return Reload();
                case "release":
                    return ReleaseMouse();
                default:
                    Console.Error.WriteLine($"Unknown command: {args[0]}");
                    PrintHelp();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 2;
        }
    }

    private static int Caps(string[] args)
    {
        bool dumpHex = args.Contains("--hex", StringComparer.OrdinalIgnoreCase);
        var devices = DeviceCatalog.Load();
        Console.WriteLine($"Magic Trackpad HID interfaces (HidSharp): {devices.Count}");
        if (devices.Count == 0)
        {
            Console.WriteLine("  none - the UMDF PTP collection is not enumerable by HidSharp;");
            Console.WriteLine("  vendor interfaces (MI_00/02/03) also missing means USB is not present.");
        }
        foreach (var device in devices)
        {
            Console.WriteLine();
            Console.WriteLine($"[{devices.IndexOf(device)}] {device.Device.DevicePath}");
            Console.WriteLine($"    vid=0x{device.Device.VendorID:X4} pid=0x{device.Device.ProductID:X4} " +
                              $"release=0x{device.Device.ReleaseNumberBcd:X4} " +
                              $"interface={(device.InterfaceNumber?.ToString() ?? "?")} " +
                              $"collection={(device.CollectionNumber?.ToString() ?? "?")}");
            Console.WriteLine($"    manufacturer=\"{device.Device.GetManufacturer()}\" product=\"{device.Device.GetProductName()}\"");
            Console.WriteLine($"    max reports: input={device.Device.GetMaxInputReportLength()} " +
                              $"output={device.Device.GetMaxOutputReportLength()} " +
                              $"feature={device.Device.GetMaxFeatureReportLength()}");

            if (device.Descriptor is null)
            {
                Console.WriteLine($"    descriptor: ERROR {device.DescriptorError}");
                continue;
            }

            var descriptor = device.Descriptor;
            Console.WriteLine($"    descriptor: {descriptor.Length} bytes, report ids={descriptor.UsesReportIds}, " +
                              $"ptp={descriptor.IsPrecisionTouchpad}");
            if (descriptor.TopLevelCollections.Count > 0)
            {
                Console.WriteLine("    top-level collections: " +
                    string.Join(", ", descriptor.TopLevelCollections.Select(DescribeUsage)));
            }

            if (dumpHex)
            {
                Console.WriteLine("    raw descriptor:");
                PrintHexBlock(device.Device.GetRawReportDescriptor(), "      ");
            }

            PrintReports(descriptor.InputReports, "input");
            PrintReports(descriptor.OutputReports, "output");
            PrintReports(descriptor.FeatureReports, "feature");
        }

        Console.WriteLine();
        Console.WriteLine("Raw input devices for VID_05AC&PID_0265:");
        var rawDevices = RawInputDevices.Enumerate()
            .Where(d => d.Name.Contains("vid_05ac&pid_0265", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (rawDevices.Count == 0)
            Console.WriteLine("  NONE - device is not enumerated; check USB cable/port and host controller.");
        foreach (var rawDevice in rawDevices)
            Console.WriteLine($"  [{rawDevice.TypeName}] {rawDevice.Name}");

        Console.WriteLine();
        Console.WriteLine("Summary:");
        Console.WriteLine($"  precision touchpad interface: " +
            (DeviceCatalog.SelectTouchpad(devices)?.Device.DevicePath ?? "NOT FOUND"));
        Console.WriteLine($"  feature report 0x21: " +
            (devices.Any(d => d.HasFeature(0x21)) ? "FOUND" : "not found"));
        Console.WriteLine($"  output report 0x53: " +
            (devices.Any(d => d.HasOutput(0x53)) ? "FOUND" : "not found"));
        return 0;
    }

    private static void PrintReports(IEnumerable<HidReport> reports, string label)
    {
        foreach (var report in reports)
        {
            string id = report.HasReportId ? $"0x{report.ReportId:X2}" : "(none)";
            Console.WriteLine($"    {label} report id={id} length={report.PayloadByteLength} bytes " +
                              $"({report.Fields.Count} fields)");
            foreach (var field in report.Fields)
            {
                uint usage = field.UsageAt(field.Count - 1);
                string flags = field.IsConstant ? "Constant"
                    : field.IsVariable ? "Variable" : "Array";
                if (field.IsRelative) flags += ",Relative";
                string usageText = field.Usages.Count > 1
                    ? $"{DescribeUsage(field.Usages[0])}..{DescribeUsage(usage)}"
                    : DescribeUsage(usage);

                Console.WriteLine(
                    $"      @{field.BitOffset,4} size={field.BitSize,2} count={field.Count,-2} " +
                    $"{usageText,-42} logical={field.LogicalMinimum}..{field.LogicalMaximum} {flags}");
            }
        }
    }

    private static int RawList()
    {
        Console.WriteLine("Raw input devices:");
        foreach (var device in RawInputDevices.Enumerate())
            Console.WriteLine($"  [{device.TypeName}] {device.Name}");
        return 0;
    }

    private static int Descriptor(string[] args)
    {
        string? path = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))
            ?? GetOption(args, "--path");
        if (string.IsNullOrWhiteSpace(path))
        {
            Console.Error.WriteLine("Usage: RawInputProbe descriptor <device-interface-path>");
            return 1;
        }

        using var handle = NativeMethods.CreateFileW(
            path, 0x80000000, 3 /* FILE_SHARE_READ|WRITE */, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            Console.Error.WriteLine($"CreateFile failed: {new System.ComponentModel.Win32Exception().Message}");
            return 1;
        }

        var buffer = new byte[4096];
        uint length = (uint)buffer.Length;
        const uint IoctlHidGetReportDescriptor = 0x000B0007;
        if (!NativeMethods.DeviceIoControl(
                handle, IoctlHidGetReportDescriptor, IntPtr.Zero, 0,
                buffer, (uint)buffer.Length, out length, IntPtr.Zero))
        {
            Console.Error.WriteLine($"IOCTL_HID_GET_REPORT_DESCRIPTOR failed: {new System.ComponentModel.Win32Exception().Message}");
            return 1;
        }

        var data = buffer.AsSpan(0, (int)length).ToArray();
        Console.WriteLine($"Descriptor: {length} bytes");
        PrintHexBlock(data, "  ");

        var descriptor = HidDescriptor.Parse(data);
        Console.WriteLine($"PTP: {descriptor.IsPrecisionTouchpad}, report ids: {descriptor.UsesReportIds}");
        Console.WriteLine("Top-level collections: " +
            string.Join(", ", descriptor.TopLevelCollections.Select(DescribeUsage)));
        PrintReports(descriptor.InputReports, "input");
        PrintReports(descriptor.OutputReports, "output");
        PrintReports(descriptor.FeatureReports, "feature");
        return 0;
    }

    private static int Raw(string[] args)
    {
        double seconds = ParseOption(args, "--seconds", 0);
        string? savePath = GetOption(args, "--save");
        bool dumpHex = args.Contains("--hex", StringComparer.OrdinalIgnoreCase);
        bool allDevices = args.Contains("--all", StringComparer.OrdinalIgnoreCase);
        bool debug = args.Contains("--debug", StringComparer.OrdinalIgnoreCase);
        bool quiet = args.Contains("--quiet", StringComparer.OrdinalIgnoreCase);

        string? touchpadPath = RawInputDevices.FindMagicTrackpadTouchpad();
        if (touchpadPath is null)
        {
            Console.Error.WriteLine("Magic Trackpad 2 PTP raw-input device not found (VID_05AC&PID_0265 MI_01 Col01).");
            return 1;
        }

        // HidSharp cannot enum/read the UMDF PTP collection. Fall back to the
        // fixed descriptor layout used by MagicTrackpad2ForWindows v2.3.
        var hidDescriptor = DeviceCatalog.Load()
            .FirstOrDefault(d => d.Descriptor?.IsPrecisionTouchpad == true)?.Descriptor;
        Func<byte[], PtpFrame> parser = hidDescriptor is not null
            ? data => PtpParser.Parse(hidDescriptor, data)
            : Mt2PtpParser.Parse;

        string pathFragment = allDevices ? string.Empty : "mi_01&col01";

        Console.WriteLine($"Touchpad: {touchpadPath}");
        Console.WriteLine(hidDescriptor is not null
            ? $"Parser: HID descriptor ({hidDescriptor.Length} bytes)"
            : "Parser: built-in MT2 PTP layout (report 0x05, 5 x 21-byte fingers)");
        Console.WriteLine($"Filter: {pathFragment}");
        Console.WriteLine(seconds > 0
            ? $"Listening for {seconds:0.#} s (Ctrl+C to stop)..."
            : "Listening until Ctrl+C...");
        Console.WriteLine();

        StreamWriter? writer = null;
        if (!string.IsNullOrWhiteSpace(savePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(savePath))!);
            writer = new StreamWriter(savePath, append: false) { AutoFlush = true };
            Console.WriteLine($"Saving JSONL frames to {savePath}");
        }

        using (writer)
        {
            var monitor = new RawInputMonitor(parser, pathFragment, frame =>
            {
                if (!quiet) Console.WriteLine(frame);
                if (dumpHex && !quiet)
                    Console.WriteLine($"    hex={Convert.ToHexString(frame.Raw)}");

                if (writer is not null)
                {
                    var record = new
                    {
                        t = frame.Timestamp.ToString("O"),
                        rid = frame.ReportId,
                        len = frame.Raw.Length,
                        hex = Convert.ToHexString(frame.Raw),
                        count = frame.ContactCount,
                        scan = frame.ScanTime,
                        force = frame.Force,
                        button = frame.Button,
                        contacts = frame.Contacts.Select(c => new
                        {
                            id = c.Id,
                            tip = c.Tip,
                            conf = c.Confidence,
                            x = c.X,
                            y = c.Y,
                            p = c.Pressure,
                            w = c.Width,
                            h = c.Height,
                            size = c.Size,
                            major = c.TouchMajor,
                            minor = c.TouchMinor,
                            orientation = c.Orientation,
                            type = c.FingerType,
                            state = c.State,
                        }),
                    };
                    writer.WriteLine(JsonSerializer.Serialize(record));
                }
            }, debug);

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                monitor.Stop();
            };

            monitor.Run(seconds > 0 ? TimeSpan.FromSeconds(seconds) : null);
        }

        Console.WriteLine("Stopped.");
        return 0;
    }

    private static int SetFeature(string[] args)
    {
        byte[] bytes = ParseHexArguments(args);
        if (bytes.Length == 0)
        {
            Console.Error.WriteLine("Usage: RawInputProbe setfeature <report-id> [data...] [--interface N] [--path text]");
            return 1;
        }

        var device = SelectReportDevice(args, bytes[0], feature: true);
        if (device?.Descriptor is null)
        {
            Console.Error.WriteLine("No matching HID interface with that feature report was found.");
            return 1;
        }

        var report = device.Descriptor.GetReport(HidReportKind.Feature, bytes[0]);
        Console.WriteLine($"Device: {device.Device.DevicePath}");
        Console.WriteLine($"Feature report 0x{bytes[0]:X2}: {(report is null ? "not declared in descriptor" : $"{report.PayloadByteLength} bytes")}");

        using var stream = device.Device.Open();
        int length = Math.Max(device.Device.GetMaxFeatureReportLength(), bytes.Length);
        var buffer = new byte[length];
        Array.Copy(bytes, buffer, bytes.Length);

        stream.SetFeature(buffer);
        Console.WriteLine($"SET FEATURE ok: {Convert.ToHexString(buffer)}");

        if (!args.Contains("--no-read", StringComparer.OrdinalIgnoreCase))
        {
            var readback = new byte[length];
            Array.Copy(bytes, readback, Math.Min(bytes.Length, readback.Length));
            stream.GetFeature(readback);
            Console.WriteLine($"GET FEATURE readback: {Convert.ToHexString(readback)}");
        }

        if (bytes[0] == 0x21 && bytes.Length >= 2 && bytes[1] == 0x01)
        {
            Console.WriteLine("WARNING: host-click mode is now ENABLED. Run `setfeature 21 00` to restore firmware click.");
        }
        else if (bytes[0] == 0x21 && bytes.Length >= 2 && bytes[1] == 0x00)
        {
            Console.WriteLine("Host-click mode disabled; firmware click restored.");
        }

        return 0;
    }

    private static int GetFeature(string[] args)
    {
        if (args.Length == 0 || !TryParseByte(args[0], out byte reportId))
        {
            Console.Error.WriteLine("Usage: RawInputProbe getfeature <report-id> [--interface N] [--path text]");
            return 1;
        }

        var device = SelectReportDevice(args, reportId, feature: true);
        if (device?.Descriptor is null)
        {
            Console.Error.WriteLine("No matching HID interface with that feature report was found.");
            return 1;
        }

        Console.WriteLine($"Device: {device.Device.DevicePath}");
        using var stream = device.Device.Open();
        var buffer = new byte[Math.Max(1, device.Device.GetMaxFeatureReportLength())];
        buffer[0] = reportId;
        stream.GetFeature(buffer);
        Console.WriteLine($"GET FEATURE 0x{reportId:X2}: {Convert.ToHexString(buffer)}");
        return 0;
    }

    private static int Output(string[] args)
    {
        byte[] bytes = ParseHexArguments(args);
        if (bytes.Length == 0)
        {
            Console.Error.WriteLine("Usage: RawInputProbe output <report-id> [data...] [--interface N] [--path text]");
            return 1;
        }

        var device = SelectReportDevice(args, bytes[0], feature: false);
        if (device?.Descriptor is null)
        {
            Console.Error.WriteLine("No matching HID interface with that output report was found.");
            return 1;
        }

        var report = device.Descriptor.GetReport(HidReportKind.Output, bytes[0]);
        int length = report?.TotalByteLength ?? Math.Max(device.Device.GetMaxOutputReportLength(), bytes.Length);
        if (bytes.Length > length)
        {
            Console.Error.WriteLine($"Report is {length} bytes but {bytes.Length} bytes were supplied.");
            return 1;
        }

        var buffer = new byte[length];
        Array.Copy(bytes, buffer, bytes.Length);

        Console.WriteLine($"Device: {device.Device.DevicePath}");
        Console.WriteLine($"Output report 0x{bytes[0]:X2}: {length} bytes");
        using var stream = device.Device.Open();
        stream.Write(buffer);
        Console.WriteLine($"WRITE OUTPUT ok: {Convert.ToHexString(buffer)}");
        return 0;
    }

    private static int Battery()
    {
        var response = ControlPanelPipe.GetBattery();
        Console.WriteLine($"Pipe: {ControlPanelPipe.DefaultPipeName}");
        Console.WriteLine($"Battery: status=0x{response.Status:X2} level={(response.Ok ? $"{response.Value}%" : "n/a")}");
        return response.Ok ? 0 : 1;
    }

    private static int Haptic(string[] args)
    {
        string? strengthText = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        uint strength = ControlPanelPipe.ParseStrength(strengthText);
        var response = ControlPanelPipe.Actuate(strength);
        Console.WriteLine($"Haptic: strength=0x{strength:X6} status=0x{response.Status:X2} ok={response.Ok}");
        return response.Ok ? 0 : 1;
    }

    private static int Drag(string[] args)
    {
        double seconds = ParseOption(args, "--seconds", 0);
        double gain = ParseOption(args, "--gain", 0.5);
        double threshold = ParseOption(args, "--threshold", 25);
        bool keepGestures = args.Contains("--keep-gestures", StringComparer.OrdinalIgnoreCase);
        bool debug = args.Contains("--debug", StringComparer.OrdinalIgnoreCase);

        if (RawInputDevices.FindMagicTrackpadTouchpad() is null)
        {
            Console.Error.WriteLine("Magic Trackpad 2 PTP raw-input device not found.");
            return 1;
        }

        Console.WriteLine("3-finger drag prototype");
        Console.WriteLine($"  gain={gain:0.00} threshold={threshold:0} stable=40 ms");
        Console.WriteLine(keepGestures
            ? "  Windows 3-finger gestures: unchanged"
            : "  Windows 3-finger gestures: disabled while running, restored on exit");
        Console.WriteLine(seconds > 0
            ? $"Running for {seconds:0.#} s (Ctrl+C to stop)..."
            : "Running until Ctrl+C...");
        Console.WriteLine();

        using var drag = new ThreeFingerDrag(gain, threshold, 40, !keepGestures);
        drag.Status += message => Console.WriteLine($"[drag] {message}");

        var monitor = new RawInputMonitor(
            Mt2PtpParser.Parse,
            "mi_01&col01",
            frame => drag.Process(frame),
            debug);

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            drag.Release("Ctrl+C");
            monitor.Stop();
        };

        try
        {
            monitor.Run(seconds > 0 ? TimeSpan.FromSeconds(seconds) : null);
        }
        finally
        {
            drag.Release("exit");
        }

        return 0;
    }

    private static int Reload()
    {
        var response = ControlPanelPipe.ReloadSettings();
        Console.WriteLine($"Reload settings: status=0x{response.Status:X2} ok={response.Ok}");
        return response.Ok ? 0 : 1;
    }

    private static int ReleaseMouse()
    {
        for (int i = 0; i < 3; i++)
        {
            try { MouseInjector.LeftUp(); }
            catch (Exception ex) { Console.Error.WriteLine($"LeftUp failed: {ex.Message}"); }
        }
        Console.WriteLine("Left button up sent (fail-open).");
        return 0;
    }

    private static DeviceInfo? SelectReportDevice(string[] args, byte reportId, bool feature)
    {
        int? interfaceNumber = int.TryParse(GetOption(args, "--interface"), out int parsed) ? parsed : null;
        string? pathContains = GetOption(args, "--path");
        var devices = DeviceCatalog.Load();
        return DeviceCatalog.Select(
            devices,
            interfaceNumber,
            pathContains,
            feature ? reportId : null,
            feature ? null : reportId);
    }

    private static byte[] ParseHexArguments(string[] args)
    {
        var raw = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                bool takesValue = args[i] is not ("--no-read" or "--hex" or "--all");
                if (takesValue) i++;
                continue;
            }
            raw.Add(args[i]);
        }

        var tokens = raw
            .SelectMany(a => a.Split(new[] { ' ', ',', ';', '-' }, StringSplitOptions.RemoveEmptyEntries))
            .ToList();

        if (tokens.Count == 1 && tokens[0].Length > 2 && tokens[0].Length % 2 == 0)
            tokens = Enumerable.Range(0, tokens[0].Length / 2)
                .Select(i => tokens[0].Substring(i * 2, 2))
                .ToList();

        var result = new List<byte>();
        foreach (string token in tokens)
        {
            string text = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? token[2..]
                : token;
            if (!TryParseByte(text, out byte value))
            {
                throw new FormatException($"Invalid hex byte: {token}");
            }
            result.Add(value);
        }
        return result.ToArray();
    }

    private static bool TryParseByte(string text, out byte value) =>
        byte.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out value);

    private static string? GetOption(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    private static double ParseOption(string[] args, string name, double fallback) =>
        double.TryParse(GetOption(args, name), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double value)
            ? value
            : fallback;

    private static void PrintHexBlock(byte[] data, string indent)
    {
        for (int i = 0; i < data.Length; i += 16)
        {
            var line = data.Skip(i).Take(16).ToArray();
            Console.WriteLine($"{indent}{i:X4}  {Convert.ToHexString(line)}");
        }
    }

    private static string DescribeUsage(uint packed)
    {
        uint page = packed >> 16;
        uint usage = packed & 0xFFFF;
        string name = (page, usage) switch
        {
            (0x01, 0x30) => "X",
            (0x01, 0x31) => "Y",
            (0x01, 0x32) => "Z",
            (0x09, 0x01) => "Button",
            (0x0D, 0x01) => "Digitizer",
            (0x0D, 0x05) => "Touch Pad",
            (0x0D, 0x30) => "Tip Pressure",
            (0x0D, 0x42) => "Tip Switch",
            (0x0D, 0x47) => "Confidence",
            (0x0D, 0x48) => "Width",
            (0x0D, 0x49) => "Height",
            (0x0D, 0x51) => "Contact Identifier",
            (0x0D, 0x54) => "Contact Count",
            (0x0D, 0x55) => "Contact Count Maximum",
            (0x0D, 0x56) => "Scan Time",
            _ => string.Empty,
        };
        string text = name.Length > 0 ? $" {name}" : string.Empty;
        return $"0x{page:X4}:0x{usage:X4}{text}";
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            RawInputProbe - Magic Trackpad 2 HID / Raw Input probe

            Commands:
              caps [--hex]
                  Enumerate HID interfaces, descriptors, reports, and PTP capabilities.

              raw [--seconds N] [--save frames.jsonl] [--hex] [--all] [--debug] [--quiet]
                  Register Raw Input for the precision touchpad and print parsed contacts.
                  Ctrl+C stops; --seconds stops automatically.

              drag [--seconds N] [--gain 0.5] [--threshold 25] [--keep-gestures] [--debug]
                  Minimal 3-finger drag prototype: exactly three active contacts must be
                  stable for 40 ms, then move; left button is released on lift/exit.

              setfeature <id> [data...] [--interface N] [--path text] [--no-read]
                  Send a HID feature report, e.g. `setfeature 21 01` or `setfeature 21 00`.

              getfeature <id> [--interface N] [--path text]
                  Read a HID feature report.

              output <id> [data...] [--interface N] [--path text]
                  Write a HID output report, e.g. `output 53 ...`.

              battery
                  Read the battery level through the v2.3 control panel pipe.

              haptic [light|medium|firm|max|0xRRGGBB]
                  Fire the haptic actuator once through the control panel pipe.

              reload
                  Reload FeedbackClick/FeedbackRelease settings via the pipe.

              release
                  Fail-open: send left mouse button up (use if a drag gets stuck).

            Hex arguments accept `21 01`, `0x21,0x01`, or `2101`.
            """);
    }
}
