using OpenTrackpad.Core.Battery;

namespace OpenTrackpad.Core.Tests.Battery;

public sealed class BatteryAlertPolicyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(100)]
    [InlineData(50)]
    [InlineData(21)]
    public void AboveLowThreshold_NoAlert(int level)
    {
        var policy = new BatteryAlertPolicy();

        Assert.Null(policy.Evaluate(level, T0));
    }

    [Fact]
    public void AtLowThreshold_AlertsOnce()
    {
        var policy = new BatteryAlertPolicy();

        Assert.Equal(BatteryAlertKind.Low, policy.Evaluate(20, T0));
        Assert.Null(policy.Evaluate(20, T0.AddMinutes(5)));
        Assert.Null(policy.Evaluate(19, T0.AddMinutes(10)));
    }

    [Fact]
    public void AtCriticalThreshold_AlertsCriticalOnce()
    {
        var policy = new BatteryAlertPolicy();

        Assert.Equal(BatteryAlertKind.Critical, policy.Evaluate(10, T0));
        Assert.Null(policy.Evaluate(10, T0.AddMinutes(5)));
        Assert.Null(policy.Evaluate(9, T0.AddMinutes(6)));
    }

    [Fact]
    public void DropFromAboveLowToCritical_AlertsCritical()
    {
        var policy = new BatteryAlertPolicy();

        Assert.Null(policy.Evaluate(50, T0));
        Assert.Equal(BatteryAlertKind.Critical, policy.Evaluate(9, T0.AddMinutes(1)));
    }

    [Fact]
    public void RiseAboveThresholds_RearmsLowAlertAfterCooldown()
    {
        var policy = new BatteryAlertPolicy();

        Assert.Equal(BatteryAlertKind.Low, policy.Evaluate(20, T0));
        Assert.Null(policy.Evaluate(25, T0.AddMinutes(10)));
        Assert.Null(policy.Evaluate(20, T0.AddMinutes(20))); // cooldown still active
        Assert.Equal(BatteryAlertKind.Low, policy.Evaluate(20, T0.AddHours(2)));
    }

    [Fact]
    public void LowAlertDoesNotRepeatDuringCooldown_ButCriticalCanFire()
    {
        var policy = new BatteryAlertPolicy();

        Assert.Equal(BatteryAlertKind.Low, policy.Evaluate(20, T0));
        Assert.Equal(BatteryAlertKind.Critical, policy.Evaluate(10, T0.AddMinutes(30)));
        Assert.Null(policy.Evaluate(10, T0.AddMinutes(40)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void InvalidLevel_Throws(int level)
    {
        var policy = new BatteryAlertPolicy();

        Assert.Throws<ArgumentOutOfRangeException>(() => policy.Evaluate(level, T0));
    }

    [Fact]
    public void CriticalAboveLow_IsRejected()
    {
        var policy = new BatteryAlertPolicy { LowThreshold = 10, CriticalThreshold = 20 };

        Assert.Throws<InvalidOperationException>(() => policy.Evaluate(50, T0));
    }
}
