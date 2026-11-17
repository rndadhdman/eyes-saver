using System.Text.Json;

namespace EyesSaver.Core;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <param name="appRoot">Settings go in a "data" folder inside this folder.</param>
    public SettingsStore(string appRoot)
        : this(Path.Combine(appRoot, "data"), direct: true)
    {
    }

    private SettingsStore(string dataDirectory, bool direct)
    {
        DataDirectory = dataDirectory;
        SettingsPath = Path.Combine(DataDirectory, "settings.json");
        IsPortable = false;
    }

    public string DataDirectory { get; }

    public string SettingsPath { get; }

    /// <summary>True when settings live next to the executable.</summary>
    public bool IsPortable { get; private init; }

    public bool SettingsFileExists => File.Exists(SettingsPath);

    /// <summary>
    /// Portable mode: when a writable "data" folder sits next to the executable, settings are kept
    /// there (the portable downloads ship with one). Installed copies have no such folder and use
    /// the per-user folder: %LOCALAPPDATA%\EyesSaver on Windows, $XDG_CONFIG_HOME/eyessaver on Linux.
    /// </summary>
    public static SettingsStore CreateForApplication(string appRoot)
    {
        var portable = new SettingsStore(appRoot) { IsPortable = true };
        if (Directory.Exists(portable.DataDirectory) && (portable.SettingsFileExists || CanWrite(portable.DataDirectory)))
        {
            return portable;
        }

        return new SettingsStore(UserDataDirectory(), direct: true);
    }

    public static string UserDataDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EyesSaver",
                "data");
        }

        // Inside a Flatpak, XDG_CONFIG_HOME already points at the app's own ~/.var/app/<id>/config.
        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(config) || !Path.IsPathRooted(config))
        {
            config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        return Path.Combine(config, "eyessaver");
    }

    public AppSettings LoadOrCreateDefaults(IReadOnlyList<MonitorDescriptor> monitors)
    {
        var loaded = TryLoad();
        if (loaded is not null && monitors.Count == 0)
        {
            return loaded;
        }

        var settings = SettingsReconciler.Reconcile(loaded, monitors);
        Save(settings);
        return settings;
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DataDirectory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);

        // Write to a temp file first so a crash or power loss never leaves a half-written file.
        var tempPath = SettingsPath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, SettingsPath, overwrite: true);
    }

    private AppSettings? TryLoad()
    {
        if (!File.Exists(SettingsPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
        }
        catch (JsonException)
        {
            BackupCorruptSettings();
            return null;
        }
        catch (NotSupportedException)
        {
            BackupCorruptSettings();
            return null;
        }
    }

    private void BackupCorruptSettings()
    {
        Directory.CreateDirectory(DataDirectory);
        if (!File.Exists(SettingsPath))
        {
            return;
        }

        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var backupPath = Path.Combine(DataDirectory, $"settings.corrupt-{timestamp}.json");
        File.Move(SettingsPath, backupPath, overwrite: true);
    }

    private static bool CanWrite(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
