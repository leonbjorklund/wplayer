using System.Windows.Input;

namespace WPlayer.Tests;

[TestClass]
public sealed class TitleMouseGestureTests
{
    [TestMethod]
    public void ResolvesExactTitleMouseGestures()
    {
        Assert.AreEqual(
            PlaybackCommand.TogglePlayPause,
            TitleMouseGestureResolver.Resolve(MouseButton.Middle, ModifierKeys.None));
        Assert.AreEqual(
            PlaybackCommand.Next,
            TitleMouseGestureResolver.Resolve(MouseButton.Left, ModifierKeys.Shift));
        Assert.AreEqual(
            PlaybackCommand.Previous,
            TitleMouseGestureResolver.Resolve(MouseButton.Right, ModifierKeys.Shift));
    }

    [DataRow(MouseButton.Left, ModifierKeys.None)]
    [DataRow(MouseButton.Right, ModifierKeys.None)]
    [DataRow(MouseButton.Middle, ModifierKeys.Shift)]
    [DataRow(MouseButton.Left, ModifierKeys.Control)]
    [DataRow(MouseButton.Right, ModifierKeys.Alt)]
    [DataRow(MouseButton.Left, ModifierKeys.Control | ModifierKeys.Shift)]
    [DataRow(MouseButton.Right, ModifierKeys.Alt | ModifierKeys.Shift)]
    [DataRow(MouseButton.XButton1, ModifierKeys.None)]
    [TestMethod]
    public void RejectsOtherMouseModifierCombinations(MouseButton button, ModifierKeys modifiers)
    {
        Assert.AreEqual(PlaybackCommand.None, TitleMouseGestureResolver.Resolve(button, modifiers));
    }
}
