using System.Net;
using System.Net.Http;
using Velopack;
using Velopack.Sources;

namespace WPlayer.Services;

public static class AppUpdateService
{
    private const string UpdateSourceEnvironmentVariable = "WPLAYER_UPDATE_SOURCE";
    private const string ProductionUpdateSource = "https://github.com/leonbjorklund/wplayer";

    public static async Task CheckOnStartupAsync(Action<string?> setStatus)
    {
        if (PackageIdentity.IsPackaged)
        {
            return;
        }

        try
        {
            var sourceOverride = Environment.GetEnvironmentVariable(UpdateSourceEnvironmentVariable);
            var manager = string.IsNullOrWhiteSpace(sourceOverride)
                ? new UpdateManager(new GithubSource(ProductionUpdateSource, accessToken: null, prerelease: false))
                : new UpdateManager(sourceOverride);
            if (!manager.IsInstalled)
            {
                return;
            }

            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                return;
            }

            setStatus("Updating...");
            await manager.DownloadUpdatesAsync(update);

            setStatus("Restarting to update...");
            manager.ApplyUpdatesAndRestart(update);
        }
        catch (HttpRequestException ex) when (
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(UpdateSourceEnvironmentVariable)) &&
            ex.StatusCode == HttpStatusCode.NotFound)
        {
        }
        catch (Exception ex)
        {
            AppLog.Error("Update failed", ex);
            setStatus("Update failed");
        }
    }
}
