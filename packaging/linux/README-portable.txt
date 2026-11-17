Eyes Saver for Linux (portable)
===============================

Run:        ./EyesSaver
Commands:   ./EyesSaver --toggle | --pause | --resume | --stronger | --weaker | --quit

Settings are kept in the "data" folder next to the program. Delete that folder to keep
them in ~/.config/eyessaver instead.

Needs X11, or Wayland with XWayland. Packages it uses: libx11-6 libxext6 libxrandr2 libxi6
libxcursor1 libice6 libsm6 libfontconfig1 (already present on almost every desktop).

How the tint is drawn
  - With a compositor (GNOME, KDE, Xfce with compositing, picom, ...): a click-through overlay.
  - Without one: the display gamma ramp. It is restored when Eyes Saver quits.

Shortcuts: Ctrl+Alt+Home pauses or resumes, Ctrl+Alt+PgUp / PgDn change the strength.
On Wayland, global shortcuts from X11 apps only work while an X11 window has focus. Add
custom shortcuts in your desktop settings that run the commands above instead.

Tray icon: needs a StatusNotifier tray (KDE, Xfce, Cinnamon, MATE; on GNOME the
"AppIndicator and KStatusNotifierItem Support" extension). Without a tray, launch Eyes Saver
again to open its window.
