using System.Text.Json;
using OpenTrackpad.Core.Input;

namespace OpenTrackpad.Core.Tests.Input;

public sealed class SwipeGestureSettingsTests
{
    [Fact]
    public void None_DisablesEveryGesture()
    {
        SwipeGestureSettings settings = SwipeGestureSettings.None;

        Assert.False(settings.AnyEnabled);
        Assert.False(settings.AnyFourFingerEnabled);
        Assert.False(settings.IsEnabled(new SwipeGesture(SwipeDirection.Down, 4)));
        Assert.False(settings.IsEnabled(new SwipeGesture(SwipeDirection.Left, 3)));
    }

    [Fact]
    public void Defaults_EnableEveryGesture()
    {
        var settings = new SwipeGestureSettings();

        Assert.True(settings.AnyEnabled);
        Assert.True(settings.AnyFourFingerEnabled);
        Assert.True(settings.IsEnabled(new SwipeGesture(SwipeDirection.Down, 4)));
        Assert.True(settings.IsEnabled(new SwipeGesture(SwipeDirection.Up, 4)));
        Assert.True(settings.IsEnabled(new SwipeGesture(SwipeDirection.Left, 4)));
        Assert.True(settings.IsEnabled(new SwipeGesture(SwipeDirection.Right, 4)));
        Assert.True(settings.IsEnabled(new SwipeGesture(SwipeDirection.Left, 3)));
        Assert.True(settings.IsEnabled(new SwipeGesture(SwipeDirection.Right, 3)));
    }

    [Fact]
    public void PerGestureSwitches_OnlyEnableTheirOwnAction()
    {
        var settings = new SwipeGestureSettings
        {
            Copy = true,
            Paste = false,
            CloseApp = false,
            CloseTab = false,
            SwitchDesktop = false,
        };

        Assert.True(settings.IsEnabled(new SwipeGesture(SwipeDirection.Down, 4)));
        Assert.False(settings.IsEnabled(new SwipeGesture(SwipeDirection.Up, 4)));
        Assert.False(settings.IsEnabled(new SwipeGesture(SwipeDirection.Left, 4)));
        Assert.False(settings.IsEnabled(new SwipeGesture(SwipeDirection.Right, 4)));
        Assert.False(settings.IsEnabled(new SwipeGesture(SwipeDirection.Left, 3)));
    }

    [Fact]
    public void ComputedProperties_AreNotSerialized()
    {
        string json = JsonSerializer.Serialize(new SwipeGestureSettings());

        Assert.DoesNotContain("AnyEnabled", json);
        Assert.DoesNotContain("AnyFourFingerEnabled", json);
    }
}
