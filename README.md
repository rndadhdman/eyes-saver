# Eyes Saver

Lays a warm, click-through color tint over each display. Every display can have its own color
and strength; displays you plug in or out are picked up automatically and their settings are
remembered.

Runs on Windows 10/11 and on Linux (X11, or Wayland through XWayland).

**Download:** [latest release](https://github.com/rndadhdman/eyes-saver/releases/latest)

## License

[MIT](LICENSE). Bundled components: .NET runtime, Avalonia, SkiaSharp and HarfBuzzSharp (all MIT)
and the Inter font (SIL Open Font License 1.1).

## Packages

`.\build.ps1` builds everything into `dist\`:

| File | What it is |
| --- | --- |
| `EyesSaver-<v>-win-x64.msi` | Per-machine installer: Program Files, Start menu shortcut, upgrades in place |
| `EyesSaver-<v>-win-x64-portable.zip` | Single `EyesSaver.exe`; settings stay in the `data` folder next to it |
| `eyessaver_<v>_amd64.deb` | Debian 12+ / Ubuntu 22.04+; installs to `/opt/eyessaver`, command `eyessaver` |
| `EyesSaver-<v>-x86_64.flatpak` | Flatpak bundle; `flatpak install EyesSaver-<v>-x86_64.flatpak` (runtime from Flathub) |
| `EyesSaver-<v>-linux-x64-portable.tar.gz` | Extract and run `./EyesSaver`; settings stay next to it |

Build needs the .NET 10 SDK, and Docker Desktop (Linux containers) for the Linux packages.
Options: `-Version`, `-SkipTests`, `-SkipMsi`, `-SkipLinux`, `-SkipFlatpak`.

## Settings

A `data` folder next to the executable means portable mode: settings are kept there. Otherwise
they go to `%LOCALAPPDATA%\EyesSaver\data` (Windows), `~/.config/eyessaver` (Linux) or
`~/.var/app/io.github.eyessaver.EyesSaver/config/eyessaver` (Flatpak).

## Command line

```
eyessaver [--tray | --toggle | --pause | --resume | --stronger | --weaker | --quit | --version]
```

A second launch hands its command to the running copy. On Wayland, bind these commands to
custom shortcuts in the desktop settings.

## How the tint is drawn

- **Windows**: layered, click-through, topmost windows; optionally excluded from screen capture.
- **Linux with a compositor**: override-redirect 32-bit windows with an empty input shape.
- **Linux without a compositor**: each monitor's gamma ramp, restored on exit.

## Layout

- `src/EyesSaver.Core`: settings, monitor detection (Win32 and XRandR/EDID), reconciliation.
- `src/EyesSaver.App`: Avalonia UI, tray, and `Platform/Windows` + `Platform/Linux` backends.
- `tests/EyesSaver.Tests`: console test runner (`dotnet run --project tests/EyesSaver.Tests`).
- `packaging/`: WiX installer, .deb and Flatpak build scripts, icons.
