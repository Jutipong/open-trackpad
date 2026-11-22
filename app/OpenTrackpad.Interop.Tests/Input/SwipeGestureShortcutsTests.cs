using OpenTrackpad.Core.Input;
using OpenTrackpad.Interop.Input;

namespace OpenTrackpad.Interop.Tests.Input;

public sealed class SwipeGestureShortcutsTests
{
    [Theory]
    [InlineData(4, SwipeDirection.Down, new ushort[] { 0x11, 0x43 })]
    [InlineData(4, SwipeDirection.Up, new ushort[] { 0x11, 0x56 })]
    [InlineData(4, SwipeDirection.Left, new ushort[] { 0x12, 0x73 })]
    [InlineData(4, SwipeDirection.Right, new ushort[] { 0x11, 0x57 })]
    [InlineData(3, SwipeDirection.Left, new ushort[] { 0x11, 0x5B, 0x27 })]
    [InlineData(3, SwipeDirection.Right, new ushort[] { 0x11, 0x5B, 0x25 })]
    public void KnownGesture_MapsToExpectedKeys(
        int contacts, SwipeDirection direction, ushort[] expected)
    {
        IReadOnlyList<ushort>? keys =
            SwipeGestureShortcuts.For(new SwipeGesture(direction, contacts));

        Assert.NotNull(keys);
        Assert.Equal(expected, keys);
    }

    [Theory]
    [InlineData(3, SwipeDirection.Up)]
    [InlineData(3, SwipeDirection.Down)]
    [InlineData(5, SwipeDirection.Left)]
    public void UnknownGesture_HasNoShortcut(int contacts, SwipeDirection direction)
    {
        Assert.Null(SwipeGestureShortcuts.For(new SwipeGesture(direction, contacts)));
    }
}
