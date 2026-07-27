namespace WPlayer.Tests;

[TestClass]
public sealed class MediaRefreshErrorTests
{
    [TestMethod]
    public void LogsEntryChangesAndOneRecovery()
    {
        string? currentKey = null;

        Assert.IsTrue(MainWindow.EnterRefreshError(new InvalidOperationException("first"), ref currentKey));
        Assert.IsFalse(MainWindow.EnterRefreshError(new InvalidOperationException("first"), ref currentKey));
        Assert.IsTrue(MainWindow.EnterRefreshError(new InvalidOperationException("changed"), ref currentKey));
        Assert.IsTrue(MainWindow.LeaveRefreshError(ref currentKey));
        Assert.IsFalse(MainWindow.LeaveRefreshError(ref currentKey));
    }
}
