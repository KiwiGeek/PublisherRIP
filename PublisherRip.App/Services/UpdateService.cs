using Velopack;
using Velopack.Sources;

namespace PublisherRip.App.Services;

public enum UpdateCheckOutcome
{
    UpToDate,
    UpdateReady,
    Failed,
}

public sealed record UpdateCheckResult(UpdateCheckOutcome Outcome, string? Version = null, string? Error = null);

/// <summary>
/// Checks GitHub Releases for a newer version and downloads it in the background.
/// A downloaded update is applied either when the user chooses "Restart to update",
/// or automatically by Velopack the next time the app starts.
/// </summary>
public sealed class UpdateService
{
    private const string RepositoryUrl = "https://github.com/KiwiGeek/PublisherRIP";

    private readonly UpdateManager _manager;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private VelopackAsset? _readyToApply;

    private UpdateService(UpdateManager manager)
    {
        _manager = manager;
    }

    /// <summary>For testing: a local folder or URL to use instead of GitHub Releases.</summary>
    private const string SourceOverrideEnvVar = "PUBLISHERRIP_UPDATE_SOURCE";

    /// <summary>Null when running an uninstalled (dev) build.</summary>
    public static UpdateService? TryCreateForInstalledApp()
    {
        try
        {
            var sourceOverride = Environment.GetEnvironmentVariable(SourceOverrideEnvVar);
            if (!string.IsNullOrWhiteSpace(sourceOverride))
            {
                var overridden = new UpdateManager(sourceOverride);
                return overridden.IsInstalled ? new UpdateService(overridden) : null;
            }

            // Pre-release installs (from v1.2.3-beta.1 style tags) also receive pre-releases;
            // stable installs only see stable releases.
            var installed = new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
            if (!installed.IsInstalled)
            {
                return null;
            }

            var includePrereleases = installed.CurrentVersion?.IsPrerelease == true;
            var manager = includePrereleases
                ? new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: true))
                : installed;

            return new UpdateService(manager);
        }
        catch
        {
            return null;
        }
    }

    public string? CurrentVersion => _manager.CurrentVersion?.ToString();

    public string? ReadyVersion => _readyToApply?.Version?.ToString();

    /// <summary>Checks for an update and, if found, downloads it. Safe to call repeatedly.</summary>
    public async Task<UpdateCheckResult> CheckAndDownloadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_readyToApply is not null)
            {
                return new UpdateCheckResult(UpdateCheckOutcome.UpdateReady, ReadyVersion);
            }

            var update = await _manager.CheckForUpdatesAsync();
            if (update is null)
            {
                return new UpdateCheckResult(UpdateCheckOutcome.UpToDate);
            }

            await _manager.DownloadUpdatesAsync(update);
            _readyToApply = update.TargetFullRelease;
            return new UpdateCheckResult(UpdateCheckOutcome.UpdateReady, ReadyVersion);
        }
        catch (Exception ex)
        {
            // Offline, GitHub rate limits, etc. Callers decide whether to surface this.
            return new UpdateCheckResult(UpdateCheckOutcome.Failed, Error: ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Exits the app, applies the downloaded update, and relaunches.</summary>
    public void RestartToApply()
    {
        if (_readyToApply is not null)
        {
            _manager.ApplyUpdatesAndRestart(_readyToApply);
        }
    }
}
