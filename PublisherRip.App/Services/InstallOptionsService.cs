using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Velopack.Locators;

namespace PublisherRip.App.Services;

/// <summary>
/// The optional launch points chosen at install time: Desktop shortcut, Start Menu shortcut,
/// and a <c>publisherrip</c> command on the user's PATH.
/// </summary>
public sealed record InstallOptions(bool DesktopShortcut, bool StartMenuShortcut, bool CommandLineLauncher)
{
    public static InstallOptions None { get; } = new(false, false, false);
}

/// <summary>
/// Creates and removes the optional launch points for an installed (Velopack) copy of the app.
/// Shortcuts and the launcher target <c>{root}\current\PublisherRip.App.exe</c>, which Velopack
/// keeps at a stable path across updates. Shortcuts carry the same AppUserModelID Velopack assigns
/// the running process, so taskbar grouping and pinning work.
/// </summary>
public sealed class InstallOptionsService
{
    private const string ShortcutFileName = "Publisher RIP.lnk";
    private const string ShortcutDescription = "Launch Publisher RIP";
    private const string LauncherFileName = "publisherrip.cmd";
    private const string ExeFileName = "PublisherRip.App.exe";

    private const string DesktopEnvVar = "PUBLISHERRIP_INSTALL_DESKTOP_SHORTCUT";
    private const string StartMenuEnvVar = "PUBLISHERRIP_INSTALL_STARTMENU_SHORTCUT";
    private const string LauncherEnvVar = "PUBLISHERRIP_INSTALL_CLI_ALIAS";

    private readonly string _exePath;
    private readonly string _launcherDirectory;
    private readonly string? _appUserModelId;

    private InstallOptionsService(IVelopackLocator locator)
    {
        _exePath = Path.Combine(locator.AppContentDir!, ExeFileName);
        _launcherDirectory = Path.Combine(locator.RootAppDir!, "bin");
        _appUserModelId = locator.AppUserModelId;
    }

    /// <summary>
    /// Returns a service for the installed app, or null when running an uninstalled build
    /// (e.g. from Visual Studio), where creating shortcuts would point at a dev folder.
    /// </summary>
    public static InstallOptionsService? TryCreateForInstalledApp()
    {
        if (!VelopackLocator.IsCurrentSet)
        {
            return null;
        }

        var locator = VelopackLocator.Current;
        if (locator.IsPortable
            || string.IsNullOrEmpty(locator.RootAppDir)
            || string.IsNullOrEmpty(locator.AppContentDir)
            || string.IsNullOrEmpty(locator.UpdateExePath)
            || !File.Exists(locator.UpdateExePath))
        {
            return null;
        }

        return new InstallOptionsService(locator);
    }

    private static string DesktopShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutFileName);

    private static string StartMenuShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutFileName);

    private string LauncherPath => Path.Combine(_launcherDirectory, LauncherFileName);

    public InstallOptions GetCurrent() => new(
        File.Exists(DesktopShortcutPath),
        File.Exists(StartMenuShortcutPath),
        File.Exists(LauncherPath));

    /// <summary>Makes the installed launch points match <paramref name="options"/>.</summary>
    public void Apply(InstallOptions options)
    {
        SetShortcut(DesktopShortcutPath, options.DesktopShortcut);
        SetShortcut(StartMenuShortcutPath, options.StartMenuShortcut);

        if (options.CommandLineLauncher)
        {
            InstallLauncher();
        }
        else
        {
            RemoveLauncher();
        }
    }

    /// <summary>Removes everything this service may have created. Used by the uninstall hook.</summary>
    public void RemoveAll() => Apply(InstallOptions.None);

    /// <summary>
    /// Reads the unattended-install environment variables (same names as the old install script).
    /// Returns null if none are set, meaning the user should be asked.
    /// </summary>
    public static InstallOptions? ReadFromEnvironment()
    {
        var desktop = Environment.GetEnvironmentVariable(DesktopEnvVar);
        var startMenu = Environment.GetEnvironmentVariable(StartMenuEnvVar);
        var launcher = Environment.GetEnvironmentVariable(LauncherEnvVar);

        if (string.IsNullOrWhiteSpace(desktop) && string.IsNullOrWhiteSpace(startMenu) && string.IsNullOrWhiteSpace(launcher))
        {
            return null;
        }

        return new InstallOptions(IsTruthy(desktop), IsTruthy(startMenu), IsTruthy(launcher));
    }

    private static bool IsTruthy(string? value) =>
        value?.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "y" or "on";

    private void SetShortcut(string shortcutPath, bool shouldExist)
    {
        if (shouldExist)
        {
            ShellShortcut.Create(shortcutPath, _exePath, ShortcutDescription, _appUserModelId);
        }
        else
        {
            DeleteFileIfExists(shortcutPath);
        }
    }

    private void InstallLauncher()
    {
        Directory.CreateDirectory(_launcherDirectory);

        // %~dp0 is the bin folder; current\ is replaced in place by Velopack on update.
        const string script = "@echo off\r\nstart \"\" \"%~dp0..\\current\\" + ExeFileName + "\"\r\n";
        File.WriteAllText(LauncherPath, script, Encoding.ASCII);

        UserPath.Add(_launcherDirectory);
    }

    private void RemoveLauncher()
    {
        DeleteFileIfExists(LauncherPath);

        if (Directory.Exists(_launcherDirectory) && !Directory.EnumerateFileSystemEntries(_launcherDirectory).Any())
        {
            Directory.Delete(_launcherDirectory);
        }

        UserPath.Remove(_launcherDirectory);
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Edits the per-user PATH in the registry directly, preserving its value kind.
    /// Environment.SetEnvironmentVariable(..., User) would expand entries like %USERPROFILE%
    /// and write them back as plain REG_SZ, permanently flattening the user's PATH.
    /// </summary>
    private static class UserPath
    {
        private const string EnvironmentKey = "Environment";
        private const string PathValueName = "Path";

        public static void Add(string directory) =>
            Modify(entries =>
            {
                if (entries.Any(e => SameDirectory(e, directory)))
                {
                    return false;
                }

                entries.Add(directory);
                return true;
            });

        public static void Remove(string directory) =>
            Modify(entries => entries.RemoveAll(e => SameDirectory(e, directory)) > 0);

        private static void Modify(Func<List<string>, bool> change)
        {
            using var key = Registry.CurrentUser.CreateSubKey(EnvironmentKey, writable: true);

            var exists = key.GetValueNames().Contains(PathValueName, StringComparer.OrdinalIgnoreCase);
            var kind = exists ? key.GetValueKind(PathValueName) : RegistryValueKind.ExpandString;
            var raw = exists
                ? key.GetValue(PathValueName, string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? string.Empty
                : string.Empty;

            var entries = raw.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (!change(entries))
            {
                return;
            }

            if (entries.Count == 0)
            {
                key.DeleteValue(PathValueName, throwOnMissingValue: false);
            }
            else
            {
                var writeKind = kind == RegistryValueKind.String ? RegistryValueKind.String : RegistryValueKind.ExpandString;
                key.SetValue(PathValueName, string.Join(';', entries), writeKind);
            }

            BroadcastEnvironmentChanged();
        }

        private static bool SameDirectory(string entry, string directory)
        {
            static string Normalize(string path) =>
                Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')).TrimEnd('\\', '/');

            return string.Equals(Normalize(entry), Normalize(directory), StringComparison.OrdinalIgnoreCase);
        }

        // Tell Explorer (and new terminals it launches) that the environment changed.
        private static void BroadcastEnvironmentChanged() =>
            SendMessageTimeout(HwndBroadcast, WmSettingChange, IntPtr.Zero, EnvironmentKey, SmtoAbortIfHung, 5000, out _);

        private static readonly IntPtr HwndBroadcast = new(0xFFFF);
        private const uint WmSettingChange = 0x001A;
        private const uint SmtoAbortIfHung = 0x0002;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
    }
}
