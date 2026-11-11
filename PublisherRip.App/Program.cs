using Velopack;

namespace PublisherRip.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must run before any WPF code. When Velopack launches the app to run an
        // install/update/uninstall hook, this handles it and exits the process.
        // In a normal launch (including dev builds that aren't installed) it returns immediately.
        VelopackApp.Build()
            .SetArgs(args)
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
