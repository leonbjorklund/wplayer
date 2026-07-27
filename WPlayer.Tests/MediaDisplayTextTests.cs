using WPlayer.Services;

namespace WPlayer.Tests;

[TestClass]
public sealed class MediaDisplayTextTests
{
    [DataRow("Artist", "Title", "Artist – Title")]
    [DataRow(null, "Title", "Title")]
    [DataRow("Artist", null, "Artist")]
    [DataRow(" ", " ", "Unknown media")]
    [TestMethod]
    public void FormatDisplayTextUsesAvailableMetadata(string? artist, string? title, string expected)
    {
        Assert.AreEqual(expected, MediaSessionService.FormatDisplayText(artist, title));
    }
}
