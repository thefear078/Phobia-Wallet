using System;
using System.IO;
using System.Text.Json;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.App;

/// <summary>
/// Interface preferences (theme, language). Deliberately separate from the vault: these are not
/// secrets, they must be readable before unlock so the login screen already looks and reads right.
/// </summary>
public sealed class UiSettings
{
    public string Theme { get; set; } = "purple";
    public string Language { get; set; } = "en";
    public string SidebarPosition { get; set; } = "Left";

    private static string Path => System.IO.Path.Combine(AppPaths.DataRoot, "ui-settings.json");

    public static UiSettings Load()
    {
        try
        {
            if (!File.Exists(Path)) return new UiSettings();
            return JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(Path)) ?? new UiSettings();
        }
        catch
        {
            // Preferences are never worth failing startup over.
            return new UiSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataRoot);
            File.WriteAllText(Path, JsonSerializer.Serialize(this));
        }
        catch
        {
            // Read-only install directory: run with defaults rather than crash.
        }
    }

    /// <summary>Applies the stored preferences and returns them.</summary>
    public static UiSettings LoadAndApply()
    {
        var settings = Load();
        if (Theming.IsKnown(settings.Theme)) Theming.Apply(settings.Theme);
        else Theming.ApplyDefaults();
        Loc.Instance.CurrentCode = settings.Language;
        return settings;
    }
}
