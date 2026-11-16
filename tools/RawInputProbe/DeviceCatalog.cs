using HidSharp;
using RawInputProbe.Hid;

namespace RawInputProbe;

public sealed class DeviceInfo
{
    public required HidDevice Device { get; init; }
    public HidDescriptor? Descriptor { get; init; }
    public string? DescriptorError { get; init; }
    public int? InterfaceNumber { get; init; }
    public int? CollectionNumber { get; init; }

    public string Path => Device.DevicePath;

    public bool IsTouchpadInterface =>
        InterfaceNumber == 1 &&
        (CollectionNumber == 1 || (Descriptor?.IsPrecisionTouchpad ?? false));

    public bool HasFeature(byte reportId) =>
        Descriptor?.FeatureReports.Any(r => r.ReportId == reportId) ?? false;

    public bool HasOutput(byte reportId) =>
        Descriptor?.OutputReports.Any(r => r.ReportId == reportId) ?? false;
}

public static class DeviceCatalog
{
    public const int AppleVendorId = 0x05AC;
    public const int MagicTrackpad2ProductId = 0x0265;

    public static List<DeviceInfo> Load()
    {
        var result = new List<DeviceInfo>();
        foreach (var device in DeviceList.Local.GetHidDevices(AppleVendorId, MagicTrackpad2ProductId, null, null))
        {
            HidDescriptor? descriptor = null;
            string? error = null;
            try
            {
                descriptor = HidDescriptor.Parse(device.GetRawReportDescriptor());
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            result.Add(new DeviceInfo
            {
                Device = device,
                Descriptor = descriptor,
                DescriptorError = error,
                InterfaceNumber = ParseHexAfter(device.DevicePath, "mi_"),
                CollectionNumber = ParseHexAfter(device.DevicePath, "col"),
            });
        }

        return result
            .OrderBy(d => d.InterfaceNumber ?? int.MaxValue)
            .ThenBy(d => d.CollectionNumber ?? int.MaxValue)
            .ToList();
    }

    public static DeviceInfo? Select(
        IEnumerable<DeviceInfo> devices,
        int? interfaceNumber = null,
        string? pathContains = null,
        byte? featureReportId = null,
        byte? outputReportId = null)
    {
        var candidates = devices.Where(d => d.Descriptor is not null);
        if (interfaceNumber.HasValue)
            candidates = candidates.Where(d => d.InterfaceNumber == interfaceNumber);
        if (!string.IsNullOrWhiteSpace(pathContains))
            candidates = candidates.Where(d =>
                d.Device.DevicePath.Contains(pathContains, StringComparison.OrdinalIgnoreCase));

        var list = candidates.ToList();
        if (featureReportId.HasValue)
        {
            var withFeature = list.Where(d => d.HasFeature(featureReportId.Value)).ToList();
            if (withFeature.Count > 0) list = withFeature;
        }
        if (outputReportId.HasValue)
        {
            var withOutput = list.Where(d => d.HasOutput(outputReportId.Value)).ToList();
            if (withOutput.Count > 0) list = withOutput;
        }

        // Prefer interface 2 for vendor reports, then interface 3.
        return list
            .OrderBy(d => d.InterfaceNumber == 2 ? 0 : d.InterfaceNumber == 3 ? 1 : 2)
            .ThenBy(d => d.InterfaceNumber ?? int.MaxValue)
            .FirstOrDefault();
    }

    public static DeviceInfo? SelectTouchpad(IEnumerable<DeviceInfo> devices) =>
        devices
            .OrderBy(d => d.IsTouchpadInterface ? 0 : 1)
            .ThenBy(d => d.InterfaceNumber ?? int.MaxValue)
            .FirstOrDefault(d => d.Descriptor?.IsPrecisionTouchpad == true)
        ?? devices.FirstOrDefault(d => d.InterfaceNumber == 1 && d.CollectionNumber == 1);

    private static int? ParseHexAfter(string text, string marker)
    {
        int index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return null;
        index += marker.Length;
        int end = index;
        while (end < text.Length && Uri.IsHexDigit(text[end])) end++;
        if (end == index) return null;
        return int.TryParse(text[index..end], System.Globalization.NumberStyles.HexNumber, null, out int value)
            ? value
            : null;
    }
}
