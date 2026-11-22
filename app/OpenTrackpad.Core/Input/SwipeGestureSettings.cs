using System.Text.Json.Serialization;

namespace OpenTrackpad.Core.Input;

/// <summary>
/// Per-gesture switches for the custom swipe actions.
/// </summary>
public sealed record SwipeGestureSettings
{
    public bool Copy { get; init; } = true;
    public bool Paste { get; init; } = true;
    public bool CloseApp { get; init; } = true;
    public bool CloseTab { get; init; } = true;
    public bool SwitchDesktop { get; init; } = true;

    public static SwipeGestureSettings None { get; } = new()
    {
        Copy = false,
        Paste = false,
        CloseApp = false,
        CloseTab = false,
        SwitchDesktop = false,
    };

    [JsonIgnore]
    public bool AnyFourFingerEnabled => Copy || Paste || CloseApp || CloseTab;

    [JsonIgnore]
    public bool AnyEnabled => AnyFourFingerEnabled || SwitchDesktop;

    public bool IsEnabled(SwipeGesture gesture) => gesture.ContactCount switch
    {
        3 => SwitchDesktop,
        4 => gesture.Direction switch
        {
            SwipeDirection.Down => Copy,
            SwipeDirection.Up => Paste,
            SwipeDirection.Left => CloseApp,
            SwipeDirection.Right => CloseTab,
            _ => false,
        },
        _ => false,
    };
}
