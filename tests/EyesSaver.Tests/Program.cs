using System.Drawing;
using EyesSaver.Core;

var tests = new TestRunner();
tests.Run("settings round trip preserves monitor tint values", SettingsRoundTripPreservesMonitorTintValues);
tests.Run("corrupt settings file is backed up and defaults are regenerated", CorruptSettingsFileIsBackedUpAndDefaultsAreRegenerated);
tests.Run("monitor tint settings clamp opacity to valid range", MonitorTintSettingsClampOpacityToValidRange);
tests.Run("custom color palette survives save load and monitor reconciliation", CustomColorPaletteSurvivesSaveLoadAndMonitorReconciliation);
tests.Run("custom color palette converts to and from color dialog values", CustomColorPaletteConvertsToAndFromColorDialogValues);
tests.Run("global monitor settings apply color opacity and enabled state to all monitors", GlobalMonitorSettingsApplyToAllMonitors);
tests.Run("global monitor settings report mixed enabled state", GlobalMonitorSettingsReportMixedEnabledState);
tests.Run("monitor reconciliation matches existing settings by device name and bounds fallback", MonitorReconciliationMatchesExistingSettings);
tests.Run("monitor layout scaler preserves relative virtual desktop layout", MonitorLayoutScalerPreservesRelativeVirtualDesktopLayout);
tests.Run("stable monitor id wins over a swapped device name", StableIdWinsOverSwappedDeviceName);
tests.Run("legacy device-name settings migrate to stable ids", LegacySettingsMigrateToStableIds);
tests.Run("disconnected monitor settings are remembered and restored", DisconnectedMonitorSettingsAreRemembered);
tests.Run("new monitor copies the shared tint", NewMonitorCopiesSharedTint);
tests.Run("new monitor gets defaults when tints differ", NewMonitorGetsDefaultsWhenTintsDiffer);
tests.Run("app-wide options survive reconciliation", AppWideOptionsSurviveReconciliation);
tests.Run("version 1 settings file loads with new defaults", Version1SettingsFileLoads);
tests.Run("tint presets are found by color", TintPresetsAreFoundByColor);
tests.Run("EDID block yields manufacturer, model name and serial", EdidBlockIsParsed);
tests.Run("EDID without serial falls back to model plus connector", EdidWithoutSerialUsesConnector);
tests.Run("settings stay next to the exe only when a data folder exists", PortableModeNeedsDataFolder);
tests.Run("live monitor enumeration returns the connected displays", LiveMonitorEnumeration);
tests.Report();

static MonitorDescriptor Stable(string id, string device, Rectangle bounds, bool primary = false)
{
    return new MonitorDescriptor(device, id, bounds, primary, StableId: id);
}

static MonitorTintSettings Tint(string key, Color color, double opacity, bool enabled = true, string device = "")
{
    return new MonitorTintSettings
    {
        MonitorKey = key,
        DeviceName = device,
        Color = ColorDto.FromColor(color),
        Opacity = opacity,
        Enabled = enabled
    };
}

static void StableIdWinsOverSwappedDeviceName()
{
    var existing = new AppSettings
    {
        Monitors =
        [
            Tint("dell", Color.Red, 0.2, device: @"\\.\DISPLAY1"),
            Tint("lg", Color.Blue, 0.6, device: @"\\.\DISPLAY2")
        ]
    };

    // Windows swapped the DISPLAYn names after a reconnect.
    var monitors = new[]
    {
        Stable("dell", @"\\.\DISPLAY2", new Rectangle(0, 0, 1920, 1080), true),
        Stable("lg", @"\\.\DISPLAY1", new Rectangle(1920, 0, 1920, 1080))
    };

    var reconciled = SettingsReconciler.Reconcile(existing, monitors);

    Assert.Equal(Color.Red.ToArgb(), reconciled.Monitors[0].Color.ToColor().ToArgb());
    Assert.Equal(Color.Blue.ToArgb(), reconciled.Monitors[1].Color.ToColor().ToArgb());
    Assert.Equal(@"\\.\DISPLAY2", reconciled.Monitors[0].DeviceName);
}

static void LegacySettingsMigrateToStableIds()
{
    var existing = new AppSettings
    {
        Monitors =
        [
            Tint(@"\\.\DISPLAY1", Color.Orange, 0.45),
            Tint(@"\\.\DISPLAY2", Color.Purple, 0.15)
        ]
    };

    var monitors = new[]
    {
        Stable("path-a", @"\\.\DISPLAY1", new Rectangle(0, 0, 1920, 1080), true),
        Stable("path-b", @"\\.\DISPLAY2", new Rectangle(1920, 0, 1920, 1080))
    };

    var reconciled = SettingsReconciler.Reconcile(existing, monitors);

    Assert.Equal("path-a", reconciled.Monitors[0].MonitorKey);
    Assert.Equal("path-b", reconciled.Monitors[1].MonitorKey);
    Assert.Near(0.45, reconciled.Monitors[0].Opacity);
    Assert.Equal(Color.Purple.ToArgb(), reconciled.Monitors[1].Color.ToColor().ToArgb());
    Assert.Equal(0, reconciled.RememberedMonitors.Count);
}

static void DisconnectedMonitorSettingsAreRemembered()
{
    var laptop = Stable("laptop", @"\\.\DISPLAY1", new Rectangle(0, 0, 1920, 1200), true);
    var external = Stable("external", @"\\.\DISPLAY2", new Rectangle(1920, 0, 2560, 1440));
    var existing = new AppSettings
    {
        Monitors =
        [
            Tint("laptop", Color.Orange, 0.3),
            Tint("external", Color.DarkRed, 0.7)
        ]
    };

    var undocked = SettingsReconciler.Reconcile(existing, [laptop]);
    Assert.Equal(1, undocked.Monitors.Count);
    Assert.Equal(1, undocked.RememberedMonitors.Count);
    Assert.Equal("external", undocked.RememberedMonitors[0].MonitorKey);

    var docked = SettingsReconciler.Reconcile(undocked, [laptop, external]);
    Assert.Equal(2, docked.Monitors.Count);
    Assert.Equal(0, docked.RememberedMonitors.Count);
    Assert.Equal(Color.DarkRed.ToArgb(), docked.Monitors[1].Color.ToColor().ToArgb());
    Assert.Near(0.7, docked.Monitors[1].Opacity);
}

static void NewMonitorCopiesSharedTint()
{
    var existing = new AppSettings
    {
        Monitors = [Tint("a", Color.Sienna, 0.55), Tint("b", Color.Sienna, 0.55)]
    };

    var reconciled = SettingsReconciler.Reconcile(existing,
    [
        Stable("a", @"\\.\DISPLAY1", new Rectangle(0, 0, 1920, 1080), true),
        Stable("b", @"\\.\DISPLAY2", new Rectangle(1920, 0, 1920, 1080)),
        Stable("c", @"\\.\DISPLAY3", new Rectangle(3840, 0, 1920, 1080))
    ]);

    Assert.Equal("c", reconciled.Monitors[2].MonitorKey);
    Assert.Equal(Color.Sienna.ToArgb(), reconciled.Monitors[2].Color.ToColor().ToArgb());
    Assert.Near(0.55, reconciled.Monitors[2].Opacity);
}

static void NewMonitorGetsDefaultsWhenTintsDiffer()
{
    var existing = new AppSettings
    {
        Monitors = [Tint("a", Color.Sienna, 0.55), Tint("b", Color.Navy, 0.2)]
    };

    var reconciled = SettingsReconciler.Reconcile(existing,
    [
        Stable("a", @"\\.\DISPLAY1", new Rectangle(0, 0, 1920, 1080), true),
        Stable("b", @"\\.\DISPLAY2", new Rectangle(1920, 0, 1920, 1080)),
        Stable("c", @"\\.\DISPLAY3", new Rectangle(3840, 0, 1920, 1080))
    ]);

    Assert.Equal(ColorDto.WarmDefault, reconciled.Monitors[2].Color);
    Assert.Near(MonitorTintSettings.DefaultOpacity, reconciled.Monitors[2].Opacity);
}

static void AppWideOptionsSurviveReconciliation()
{
    var existing = new AppSettings
    {
        TintEnabled = false,
        ExcludeFromCapture = false,
        HotkeysEnabled = false,
        TrayHintShown = true
    };

    var reconciled = SettingsReconciler.Reconcile(existing, [Stable("a", @"\\.\DISPLAY1", new Rectangle(0, 0, 800, 600), true)]);

    Assert.False(reconciled.TintEnabled);
    Assert.False(reconciled.ExcludeFromCapture);
    Assert.False(reconciled.HotkeysEnabled);
    Assert.True(reconciled.TrayHintShown);
    Assert.Equal(AppSettings.CurrentVersion, reconciled.Version);
}

static void Version1SettingsFileLoads()
{
    using var temp = TempWorkspace.Create();
    var dataDirectory = Path.Combine(temp.Root, "data");
    Directory.CreateDirectory(dataDirectory);
    File.WriteAllText(Path.Combine(dataDirectory, "settings.json"), """
        {
          "Version": 1,
          "CustomColors": [],
          "Monitors": [
            {
              "MonitorKey": "\\\\.\\DISPLAY1",
              "DisplayName": "Monitor 1",
              "Bounds": { "X": 0, "Y": 0, "Width": 3840, "Height": 2160 },
              "Enabled": true,
              "Color": { "A": 255, "R": 53, "G": 36, "B": 0 },
              "Opacity": 0.54
            }
          ]
        }
        """);

    var store = new SettingsStore(temp.Root);
    var settings = store.LoadOrCreateDefaults([Stable("path-1", @"\\.\DISPLAY1", new Rectangle(0, 0, 3840, 2160), true)]);

    Assert.True(settings.TintEnabled);
    Assert.True(settings.ExcludeFromCapture);
    Assert.Equal("path-1", settings.Monitors[0].MonitorKey);
    Assert.Equal(Color.FromArgb(255, 53, 36, 0).ToArgb(), settings.Monitors[0].Color.ToColor().ToArgb());
    Assert.Near(0.54, settings.Monitors[0].Opacity);
    Assert.False(File.Exists(store.SettingsPath + ".tmp"));
}

static void TintPresetsAreFoundByColor()
{
    Assert.Equal("Warm", TintPresets.Find(ColorDto.WarmDefault)?.Name);
    Assert.True(TintPresets.Find(ColorDto.FromColor(Color.FromArgb(255, 1, 2, 3))) is null);
}

static byte[] SampleEdid(string name, string? serialText, uint serialNumber)
{
    var edid = new byte[128];
    new byte[] { 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 }.CopyTo(edid, 0);
    edid[8] = 0x4E; // "SPT" packed as 5-bit letters
    edid[9] = 0x14;
    edid[10] = 0xA8; // product 0x0CA8, little endian
    edid[11] = 0x0C;
    BitConverter.GetBytes(serialNumber).CopyTo(edid, 12);

    void Descriptor(int offset, byte tag, string text)
    {
        edid[offset + 3] = tag;
        var bytes = System.Text.Encoding.ASCII.GetBytes((text + "\n").PadRight(13));
        bytes.AsSpan(0, 13).CopyTo(edid.AsSpan(offset + 5));
    }

    Descriptor(54, 0xFC, name);
    if (serialText is not null)
    {
        Descriptor(72, 0xFF, serialText);
    }
    else
    {
        edid[72 + 3] = 0x10; // dummy descriptor
    }

    return edid;
}

static void EdidBlockIsParsed()
{
    var info = EyesSaver.Core.Platform.Edid.Parse(SampleEdid("Sceptre Z32", "ABC123", 0));
    Assert.True(info is not null);
    Assert.Equal("SPT", info!.Manufacturer);
    Assert.Equal((ushort)0x0CA8, info.ProductCode);
    Assert.Equal("Sceptre Z32", info.MonitorName);
    Assert.Equal("SPT-0CA8-ABC123", info.SerialKey);
    Assert.Equal("x11:SPT-0CA8-ABC123", MonitorService.X11StableId("DP-1", info));
    Assert.True(EyesSaver.Core.Platform.Edid.Parse(new byte[64]) is null);
}

static void EdidWithoutSerialUsesConnector()
{
    var info = EyesSaver.Core.Platform.Edid.Parse(SampleEdid("Sceptre Z32", null, 0));
    Assert.True(info!.SerialKey is null);

    // Three identical panels without serials stay apart by connector.
    Assert.Equal("x11:SPT-0CA8@DP-1", MonitorService.X11StableId("DP-1", info));
    Assert.Equal("x11:SPT-0CA8@DP-2", MonitorService.X11StableId("DP-2", info));
    Assert.Equal("x11:HDMI-1", MonitorService.X11StableId("HDMI-1", null));

    var numbered = EyesSaver.Core.Platform.Edid.Parse(SampleEdid("Sceptre Z32", null, 0x01020304));
    Assert.Equal("SPT-0CA8-01020304", numbered!.SerialKey);
}

static void PortableModeNeedsDataFolder()
{
    using var temp = TempWorkspace.Create();
    var installed = SettingsStore.CreateForApplication(temp.Root);
    Assert.False(installed.IsPortable);
    Assert.False(installed.SettingsPath.StartsWith(temp.Root, StringComparison.OrdinalIgnoreCase));

    Directory.CreateDirectory(Path.Combine(temp.Root, "data"));
    var portable = SettingsStore.CreateForApplication(temp.Root);
    Assert.True(portable.IsPortable);
    Assert.Equal(Path.Combine(temp.Root, "data", "settings.json"), portable.SettingsPath);
}

static void LiveMonitorEnumeration()
{
    if (OperatingSystem.IsLinux() && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
    {
        Console.WriteLine("     skipped: no X display");
        return;
    }

    var monitors = new MonitorService().GetMonitors();
    foreach (var monitor in monitors)
    {
        Console.WriteLine($"     {monitor.Number}: {monitor.DisplayName} {monitor.Bounds} dpi={monitor.Dpi} primary={monitor.IsPrimary} stable={(monitor.StableId.Length > 0 ? "yes" : "no")}");
    }

    Assert.True(monitors.Count > 0);
    Assert.True(monitors.Count(monitor => monitor.IsPrimary) == 1);
    Assert.True(monitors.Select(monitor => monitor.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() == monitors.Count);
}

static void SettingsRoundTripPreservesMonitorTintValues()
{
    using var temp = TempWorkspace.Create();
    var store = new SettingsStore(temp.Root);
    var settings = new AppSettings
    {
        Monitors =
        [
            new MonitorTintSettings
            {
                MonitorKey = "\\\\.\\DISPLAY1",
                DisplayName = "Display 1",
                Bounds = new RectangleDto(0, 0, 1920, 1080),
                Enabled = true,
                Color = ColorDto.FromColor(Color.FromArgb(255, 244, 210, 128)),
                Opacity = 0.42
            }
        ]
    };

    store.Save(settings);
    var loaded = store.LoadOrCreateDefaults([]);

    Assert.Equal(1, loaded.Monitors.Count);
    Assert.Equal("\\\\.\\DISPLAY1", loaded.Monitors[0].MonitorKey);
    Assert.Equal("Display 1", loaded.Monitors[0].DisplayName);
    Assert.Equal(new RectangleDto(0, 0, 1920, 1080), loaded.Monitors[0].Bounds);
    Assert.True(loaded.Monitors[0].Enabled);
    Assert.Equal(Color.FromArgb(255, 244, 210, 128), loaded.Monitors[0].Color.ToColor());
    Assert.Near(0.42, loaded.Monitors[0].Opacity);
}

static void CorruptSettingsFileIsBackedUpAndDefaultsAreRegenerated()
{
    using var temp = TempWorkspace.Create();
    var dataDirectory = Path.Combine(temp.Root, "data");
    Directory.CreateDirectory(dataDirectory);
    File.WriteAllText(Path.Combine(dataDirectory, "settings.json"), "{ this is not valid json");

    var store = new SettingsStore(temp.Root);
    var monitor = new MonitorDescriptor("\\\\.\\DISPLAY1", "Display 1", new Rectangle(0, 0, 1920, 1080), true);

    var settings = store.LoadOrCreateDefaults([monitor]);

    Assert.Equal(1, settings.Monitors.Count);
    Assert.True(settings.Monitors[0].Enabled);
    Assert.True(Directory.GetFiles(dataDirectory, "settings.corrupt-*.json").Length == 1);
}

static void MonitorTintSettingsClampOpacityToValidRange()
{
    var settings = new MonitorTintSettings { Opacity = 2.5 };
    Assert.Near(1.0, settings.Opacity);

    settings.Opacity = -1.0;
    Assert.Near(0.0, settings.Opacity);
}

static void CustomColorPaletteSurvivesSaveLoadAndMonitorReconciliation()
{
    using var temp = TempWorkspace.Create();
    var store = new SettingsStore(temp.Root);
    var settings = new AppSettings
    {
        CustomColors =
        [
            ColorDto.FromColor(Color.FromArgb(255, 12, 34, 56)),
            ColorDto.FromColor(Color.FromArgb(255, 200, 150, 100))
        ],
        Monitors =
        [
            new MonitorTintSettings
            {
                MonitorKey = "\\\\.\\DISPLAY1",
                DisplayName = "Display 1",
                Bounds = new RectangleDto(0, 0, 1920, 1080),
                Enabled = true,
                Color = ColorDto.FromColor(Color.LightGoldenrodYellow),
                Opacity = 0.35
            }
        ]
    };

    store.Save(settings);
    var loaded = store.LoadOrCreateDefaults(
    [
        new MonitorDescriptor("\\\\.\\DISPLAY1", "Display 1", new Rectangle(0, 0, 1920, 1080), true)
    ]);

    Assert.Equal(2, loaded.CustomColors.Count);
    Assert.Equal(Color.FromArgb(255, 12, 34, 56).ToArgb(), loaded.CustomColors[0].ToColor().ToArgb());
    Assert.Equal(Color.FromArgb(255, 200, 150, 100).ToArgb(), loaded.CustomColors[1].ToColor().ToArgb());
}

static void CustomColorPaletteConvertsToAndFromColorDialogValues()
{
    var colors = new[]
    {
        ColorDto.FromColor(Color.FromArgb(255, 12, 34, 56)),
        ColorDto.FromColor(Color.FromArgb(255, 200, 150, 100))
    };

    var win32Values = CustomColorPalette.ToColorDialogValues(colors);
    var roundTripped = CustomColorPalette.FromColorDialogValues(win32Values);

    Assert.Equal(ColorTranslator.ToWin32(Color.FromArgb(255, 12, 34, 56)), win32Values[0]);
    Assert.Equal(ColorTranslator.ToWin32(Color.FromArgb(255, 200, 150, 100)), win32Values[1]);
    Assert.Equal(2, roundTripped.Count);
    Assert.Equal(Color.FromArgb(255, 12, 34, 56).ToArgb(), roundTripped[0].ToColor().ToArgb());
    Assert.Equal(Color.FromArgb(255, 200, 150, 100).ToArgb(), roundTripped[1].ToColor().ToArgb());
}

static void GlobalMonitorSettingsApplyToAllMonitors()
{
    var settings = new AppSettings
    {
        Monitors =
        [
            new MonitorTintSettings
            {
                MonitorKey = "one",
                DisplayName = "One",
                Enabled = false,
                Color = ColorDto.FromColor(Color.Red),
                Opacity = 0.2
            },
            new MonitorTintSettings
            {
                MonitorKey = "two",
                DisplayName = "Two",
                Enabled = true,
                Color = ColorDto.FromColor(Color.Blue),
                Opacity = 0.8
            }
        ]
    };

    GlobalMonitorSettings.ApplyToAll(
        settings,
        enabled: true,
        color: ColorDto.FromColor(Color.FromArgb(255, 90, 120, 200)),
        opacity: 0.55);

    Assert.True(settings.Monitors.All(monitor => monitor.Enabled));
    Assert.True(settings.Monitors.All(monitor => monitor.Color.ToColor().ToArgb() == Color.FromArgb(255, 90, 120, 200).ToArgb()));
    Assert.True(settings.Monitors.All(monitor => Math.Abs(monitor.Opacity - 0.55) < 0.0001));
}

static void GlobalMonitorSettingsReportMixedEnabledState()
{
    var settings = new AppSettings
    {
        Monitors =
        [
            new MonitorTintSettings { MonitorKey = "one", DisplayName = "One", Enabled = true, Opacity = 0.2 },
            new MonitorTintSettings { MonitorKey = "two", DisplayName = "Two", Enabled = false, Opacity = 0.8 }
        ]
    };

    var summary = GlobalMonitorSettings.Summarize(settings);

    Assert.Equal(GlobalEnabledState.Mixed, summary.EnabledState);
    Assert.True(summary.HasMixedOpacity);
    Assert.Near(0.5, summary.Opacity);
}

static void MonitorReconciliationMatchesExistingSettings()
{
    var existing = new AppSettings
    {
        Monitors =
        [
            new MonitorTintSettings
            {
                MonitorKey = "\\\\.\\DISPLAY1",
                DisplayName = "Display 1",
                Bounds = new RectangleDto(0, 0, 1920, 1080),
                Enabled = false,
                Color = ColorDto.FromColor(Color.LightBlue),
                Opacity = 0.25
            },
            new MonitorTintSettings
            {
                MonitorKey = "stale-key",
                DisplayName = "Display 2",
                Bounds = new RectangleDto(1920, 0, 1920, 1080),
                Enabled = true,
                Color = ColorDto.FromColor(Color.LightGreen),
                Opacity = 0.6
            }
        ]
    };

    var monitors = new[]
    {
        new MonitorDescriptor("\\\\.\\DISPLAY1", "Display 1", new Rectangle(0, 0, 1920, 1080), true),
        new MonitorDescriptor("\\\\.\\DISPLAY9", "Display 2", new Rectangle(1920, 0, 1920, 1080), false)
    };

    var reconciled = SettingsReconciler.Reconcile(existing, monitors);

    Assert.Equal(2, reconciled.Monitors.Count);
    Assert.False(reconciled.Monitors[0].Enabled);
    Assert.Equal("\\\\.\\DISPLAY9", reconciled.Monitors[1].MonitorKey);
    Assert.Equal(Color.LightGreen.ToArgb(), reconciled.Monitors[1].Color.ToColor().ToArgb());
    Assert.Near(0.6, reconciled.Monitors[1].Opacity);
}

static void MonitorLayoutScalerPreservesRelativeVirtualDesktopLayout()
{
    var monitors = new[]
    {
        new MonitorDescriptor("left", "Left", new Rectangle(-1280, 100, 1280, 1024), false),
        new MonitorDescriptor("primary", "Primary", new Rectangle(0, 0, 1920, 1080), true)
    };

    var layout = MonitorLayoutScaler.Scale(monitors, new Size(320, 160), 10);

    Assert.Equal(2, layout.Count);
    Assert.True(layout[0].PreviewBounds.Left < layout[1].PreviewBounds.Left);
    Assert.True(layout[1].PreviewBounds.Top < layout[0].PreviewBounds.Top);
    Assert.True(layout.All(item => item.PreviewBounds.Width > 0 && item.PreviewBounds.Height > 0));
    Assert.True(layout.All(item => item.PreviewBounds.Left >= 0 && item.PreviewBounds.Top >= 0));
    Assert.True(layout.All(item => item.PreviewBounds.Right <= 320 && item.PreviewBounds.Bottom <= 160));
}

internal sealed class TestRunner
{
    private int _passed;
    private int _failed;

    public void Run(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine($"FAIL {name}");
            Console.WriteLine(ex);
        }
    }

    public void Report()
    {
        Console.WriteLine($"{_passed} passed, {_failed} failed");
        if (_failed > 0)
        {
            Environment.Exit(1);
        }
    }
}

internal static class Assert
{
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
        }
    }

    public static void True(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Expected condition to be true.");
        }
    }

    public static void False(bool condition)
    {
        if (condition)
        {
            throw new InvalidOperationException("Expected condition to be false.");
        }
    }

    public static void Near(double expected, double actual, double tolerance = 0.0001)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
        }
    }
}

internal sealed class TempWorkspace : IDisposable
{
    private TempWorkspace(string root)
    {
        Root = root;
    }

    public string Root { get; }

    public static TempWorkspace Create()
    {
        var root = Path.Combine(Path.GetTempPath(), "EyesSaver.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new TempWorkspace(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
