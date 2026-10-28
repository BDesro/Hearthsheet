using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Hearthsheet.Infrastructure.Updates;

/// <summary>
/// Keeps the version in Windows Settings → Apps current. The installer (packaging/Hearthsheet.iss) writes the entry,
/// but in-app updates replace the files without it, so the app refreshes the version itself at startup.
/// </summary>
[SupportedOSPlatform("windows")]
public static class InstallRegistration
{
    /// <summary>Inno Setup's uninstall key for the installer's fixed AppId.</summary>
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{5A4DFE70-9A1C-405F-842C-01D7DA9F8911}_is1";

    /// <summary>Sets DisplayVersion if the entry exists; never creates one (dev runs and zip installs have none).</summary>
    public static bool RefreshDisplayVersion(RegistryKey root, string keyPath, string version)
    {
        using var key = root.OpenSubKey(keyPath, writable: true);
        if (key is null) return false;
        if (key.GetValue("DisplayVersion") as string != version) key.SetValue("DisplayVersion", version);
        return true;
    }
}
