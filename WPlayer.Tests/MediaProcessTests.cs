using WPlayer.Services;

namespace WPlayer.Tests;

[TestClass]
public sealed class MediaProcessTests
{
    [DataRow("OpenAI.Codex_2p2nqsd0c76g0!App", "OpenAI.Codex")]
    [DataRow("OpenAI.Codex_2p2nqsd0c76g0!app", "OpenAI.Codex")]
    [DataRow("Spotify.exe", "Spotify")]
    [DataRow("Package_family!Player.exe", "Player")]
    [TestMethod]
    public void GetProcessNameUsesPackageNameForGenericAppId(string sourceAppUserModelId, string expected)
    {
        Assert.AreEqual(expected, MediaProcess.GetProcessName(sourceAppUserModelId));
    }
}
