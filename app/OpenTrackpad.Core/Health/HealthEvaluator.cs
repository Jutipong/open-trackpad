namespace OpenTrackpad.Core.Health;

public enum HealthSeverity
{
    Ok = 0,
    Warning = 1,
    Error = 2,
}

public enum HealthIssue
{
    DriverMissing,
    DeviceMissing,
    AccessDeniedUsb,
    AccessDeniedBluetooth,
}

public sealed record HealthInputs(
    bool DriverInstalled,
    bool UsbDevicePresent,
    bool BluetoothDevicePresent,
    bool UsbAccessAllowed,
    bool BluetoothAccessAllowed);

public sealed record HealthFinding(HealthIssue Issue, HealthSeverity Severity);

public static class HealthEvaluator
{
    public static IReadOnlyList<HealthFinding> Evaluate(HealthInputs inputs)
    {
        if (!inputs.DriverInstalled)
            return new[] { new HealthFinding(HealthIssue.DriverMissing, HealthSeverity.Error) };

        if (!inputs.UsbDevicePresent && !inputs.BluetoothDevicePresent)
            return new[] { new HealthFinding(HealthIssue.DeviceMissing, HealthSeverity.Warning) };

        var findings = new List<HealthFinding>();
        if (inputs.UsbDevicePresent && !inputs.UsbAccessAllowed)
            findings.Add(new HealthFinding(HealthIssue.AccessDeniedUsb, HealthSeverity.Warning));
        if (inputs.BluetoothDevicePresent && !inputs.BluetoothAccessAllowed)
            findings.Add(new HealthFinding(HealthIssue.AccessDeniedBluetooth, HealthSeverity.Warning));

        return findings;
    }
}
