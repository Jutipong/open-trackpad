using OpenTrackpad.Core.Health;

namespace OpenTrackpad.Core.Tests.Health;

public sealed class HealthEvaluatorTests
{
    private static HealthInputs Inputs(
        bool driver = true,
        bool usb = false,
        bool bluetooth = false,
        bool usbAccess = true,
        bool bluetoothAccess = true) =>
        new(driver, usb, bluetooth, usbAccess, bluetoothAccess);

    [Fact]
    public void DriverMissing_ReportsErrorOnly()
    {
        IReadOnlyList<HealthFinding> findings = HealthEvaluator.Evaluate(
            Inputs(driver: false, usb: false, bluetooth: false));

        HealthFinding finding = Assert.Single(findings);
        Assert.Equal(HealthIssue.DriverMissing, finding.Issue);
        Assert.Equal(HealthSeverity.Error, finding.Severity);
    }

    [Fact]
    public void NoDevice_ReportsWarning()
    {
        HealthFinding finding = Assert.Single(HealthEvaluator.Evaluate(Inputs()));

        Assert.Equal(HealthIssue.DeviceMissing, finding.Issue);
        Assert.Equal(HealthSeverity.Warning, finding.Severity);
    }

    [Fact]
    public void UsbAccessDenied_ReportsWarning()
    {
        HealthFinding finding = Assert.Single(
            HealthEvaluator.Evaluate(Inputs(usb: true, usbAccess: false)));

        Assert.Equal(HealthIssue.AccessDeniedUsb, finding.Issue);
        Assert.Equal(HealthSeverity.Warning, finding.Severity);
    }

    [Fact]
    public void BluetoothAccessDenied_ReportsWarning()
    {
        HealthFinding finding = Assert.Single(
            HealthEvaluator.Evaluate(Inputs(bluetooth: true, bluetoothAccess: false)));

        Assert.Equal(HealthIssue.AccessDeniedBluetooth, finding.Issue);
        Assert.Equal(HealthSeverity.Warning, finding.Severity);
    }

    [Fact]
    public void BothTransportsDenied_ReportsBothWarnings()
    {
        IReadOnlyList<HealthFinding> findings = HealthEvaluator.Evaluate(
            Inputs(usb: true, bluetooth: true, usbAccess: false, bluetoothAccess: false));

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.Issue == HealthIssue.AccessDeniedUsb);
        Assert.Contains(findings, f => f.Issue == HealthIssue.AccessDeniedBluetooth);
    }

    [Fact]
    public void Healthy_ReturnsNoFindings()
    {
        Assert.Empty(HealthEvaluator.Evaluate(Inputs(usb: true)));
        Assert.Empty(HealthEvaluator.Evaluate(Inputs(bluetooth: true)));
    }
}
