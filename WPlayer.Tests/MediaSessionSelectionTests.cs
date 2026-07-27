using WPlayer.Services;

namespace WPlayer.Tests;

[TestClass]
public sealed class MediaSessionSelectionTests
{
    [TestMethod]
    public void SelectPrefersEnabledCycledSession()
    {
        var first = new Session("first");
        var cycled = new Session("cycled");
        var current = new Session("current");

        var selected = MediaSessionSelection.Select(
            new[] { first, cycled }, cycled, current, SameSession, SameApp);

        Assert.AreSame(cycled, selected);
    }

    [TestMethod]
    public void SelectUsesEnabledCurrentAppWhenCycledSessionIsMissing()
    {
        var first = new Session("first");
        var enabledCurrentApp = new Session("current");
        var missingCycled = new Session("missing");
        var current = new Session("current");

        var selected = MediaSessionSelection.Select(
            new[] { first, enabledCurrentApp }, missingCycled, current, SameSession, SameApp);

        Assert.AreSame(current, selected);
    }

    [TestMethod]
    public void SelectFallsBackToFirstEnabledSession()
    {
        var first = new Session("first");
        var selected = MediaSessionSelection.Select(
            new[] { first, new Session("second") }, null, new Session("disabled"), SameSession, SameApp);

        Assert.AreSame(first, selected);
        Assert.IsNull(MediaSessionSelection.Select<Session>([], null, null, SameSession, SameApp));
    }

    [TestMethod]
    public void NextStartsAtFirstAndWraps()
    {
        var first = new Session("first");
        var second = new Session("second");
        var sessions = new[] { first, second };

        Assert.AreSame(first, MediaSessionSelection.Next(sessions, null, SameSession));
        Assert.AreSame(second, MediaSessionSelection.Next(sessions, first, SameSession));
        Assert.AreSame(first, MediaSessionSelection.Next(sessions, second, SameSession));
        Assert.AreSame(first, MediaSessionSelection.Next(sessions, new Session("missing"), SameSession));
        Assert.IsNull(MediaSessionSelection.Next<Session>([], null, SameSession));
    }

    private static bool SameSession(Session left, Session right) => ReferenceEquals(left, right);
    private static bool SameApp(Session left, Session right) => left.App == right.App;

    private sealed record Session(string App);
}
