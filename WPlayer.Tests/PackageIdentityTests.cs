namespace WPlayer.Tests;

[TestClass]
public sealed class PackageIdentityTests
{
    [TestMethod]
    public void TestProcessIsUnpackaged()
    {
        Assert.IsFalse(PackageIdentity.IsPackaged);
    }
}
