using System.Windows;
using PublisherRip.App.Services;

namespace PublisherRip.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ThemeService.Initialize(this);
        base.OnStartup(e);
    }
}
