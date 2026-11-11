using PublisherRip.App.Services;
using Velopack;

namespace PublisherRip.App;

public static class Program
{
    /// <summary>True on the first launch after a fresh install (not after updates).</summary>
    public static bool IsFirstRun { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        // Must run before any WPF code. When Velopack launches the app to run an
        // install/update/uninstall hook, this handles it and exits the process.
        // In a normal launch (including dev builds that aren't installed) it returns immediately.
        VelopackApp.Build()
            .SetArgs(args)
            // No UI allowed here; just remember it so the main window can show the setup dialog.
            .OnFirstRun(_ => IsFirstRun = true)
            // Headless, 30-second limit. Velopack only removes shortcuts it created itself,
            // so clean up ours (and the PATH entry). Never let a failure here block uninstall.
            .OnBeforeUninstallFastCallback(_ =>
            {
                try
                {
                    InstallOptionsService.TryCreateForInstalledApp()?.RemoveAll();
                }
                catch
                {
                }
            })
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
