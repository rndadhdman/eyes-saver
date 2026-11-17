using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using EyesSaver.Core.Platform;

namespace EyesSaver.App.Platform.Linux;

/// <summary>
/// A background thread with its own X connection. It owns the global key grabs and listens for
/// RandR (display layout) and root-window stacking changes. Every call on its connection runs on
/// this thread; other threads queue work with <see cref="Invoke{T}"/>.
/// </summary>
internal sealed unsafe class X11EventThread : IDisposable
{
    private static readonly uint[] IgnoredModifiers = [0, X11.LockMask, X11.Mod2Mask, X11.LockMask | X11.Mod2Mask];

    private readonly ConcurrentQueue<Action> _work = new();
    private readonly Thread _thread;
    private readonly nint _display;
    private readonly nint _root;
    private readonly int _randrEventBase = -1;
    private readonly Dictionary<int, HotkeyAction> _grabbed = [];
    private volatile bool _running = true;
    private volatile HashSet<nint> _ownWindows = [];
    private long _lastTogglePress;

    public X11EventThread()
    {
        _display = X11Session.Open();
        if (_display == 0)
        {
            _thread = new Thread(() => { });
            return;
        }

        _root = X11.XRootWindow(_display, X11.XDefaultScreen(_display));
        if (X11.XRRQueryExtension(_display, out var eventBase, out _) != 0)
        {
            _randrEventBase = eventBase;
            X11.XRRSelectInput(_display, _root, X11.RRScreenChangeNotifyMask | X11.RRCrtcChangeNotifyMask | X11.RROutputChangeNotifyMask);
        }

        X11.XSelectInput(_display, _root, X11.SubstructureNotifyMask);
        X11.XFlush(_display);

        _thread = new Thread(Run) { IsBackground = true, Name = "Eyes Saver X11 events" };
        _thread.Start();
    }

    public event Action<HotkeyAction>? HotkeyPressed;

    public event Action? DisplaysChanged;

    /// <summary>Another window was mapped or restacked, so it may now cover the tint.</summary>
    public event Action? StackingChanged;

    public bool IsConnected => _display != 0;

    /// <summary>Windows whose own changes should not count as stacking changes (the overlays).</summary>
    public void SetOwnWindows(IEnumerable<nint> windows)
    {
        _ownWindows = [.. windows];
    }

    /// <summary>Grabs the shortcuts and returns the ones another client already holds.</summary>
    public IReadOnlyCollection<HotkeyAction> SetHotkeys(IReadOnlyList<HotkeyBinding>? bindings)
    {
        if (!IsConnected)
        {
            return bindings?.Select(binding => binding.Action).ToList() ?? [];
        }

        return Invoke(() =>
        {
            UngrabAll();
            if (bindings is null)
            {
                return (IReadOnlyCollection<HotkeyAction>)[];
            }

            var unavailable = new List<HotkeyAction>();
            foreach (var binding in bindings)
            {
                var keycode = X11.XKeysymToKeycode(_display, binding.Key);
                if (keycode == 0)
                {
                    unavailable.Add(binding.Action);
                    continue;
                }

                foreach (var extra in IgnoredModifiers)
                {
                    X11.XGrabKey(_display, keycode, X11.ControlMask | X11.Mod1Mask | extra, _root, 0, X11.GrabModeAsync, X11.GrabModeAsync);
                }

                if (X11Session.SyncAndTakeError(_display) == X11.BadAccess)
                {
                    foreach (var extra in IgnoredModifiers)
                    {
                        X11.XUngrabKey(_display, keycode, X11.ControlMask | X11.Mod1Mask | extra, _root);
                    }

                    X11Session.SyncAndTakeError(_display);
                    unavailable.Add(binding.Action);
                }
                else
                {
                    _grabbed[keycode] = binding.Action;
                }
            }

            return (IReadOnlyCollection<HotkeyAction>)unavailable;
        }) ?? [];
    }

    public void Dispose()
    {
        if (!IsConnected)
        {
            return;
        }

        Invoke(() =>
        {
            UngrabAll();
            return true;
        });
        _running = false;
        _thread.Join(TimeSpan.FromSeconds(1));
    }

    private T? Invoke<T>(Func<T> work)
    {
        if (!_running)
        {
            return default;
        }

        // Not disposed: on a timeout the queued work may still run and signal it later.
        var done = new ManualResetEventSlim();
        T? result = default;
        _work.Enqueue(() =>
        {
            try
            {
                result = work();
            }
            finally
            {
                done.Set();
            }
        });
        done.Wait(TimeSpan.FromSeconds(2));
        return result;
    }

    private void UngrabAll()
    {
        foreach (var keycode in _grabbed.Keys)
        {
            foreach (var extra in IgnoredModifiers)
            {
                X11.XUngrabKey(_display, keycode, X11.ControlMask | X11.Mod1Mask | extra, _root);
            }
        }

        _grabbed.Clear();
        X11Session.SyncAndTakeError(_display);
    }

    private void Run()
    {
        var fd = new PollFd { Fd = X11.XConnectionNumber(_display), Events = PollIn };
        X11.XEvent ev;
        while (_running)
        {
            while (_work.TryDequeue(out var work))
            {
                work();
            }

            var displaysChanged = false;
            var stackingChanged = false;
            while (X11.XPending(_display) > 0)
            {
                X11.XNextEvent(_display, &ev);
                var data = (byte*)&ev;
                switch (ev.Type)
                {
                    case X11.KeyPress:
                        OnKeyPress(*(uint*)(data + 84), *(uint*)(data + 80));
                        break;
                    case X11.MapNotify:
                    case X11.ConfigureNotify:
                        // XMapEvent / XConfigureEvent: ..., Window event (32), Window window (40)
                        if (!_ownWindows.Contains(*(nint*)(data + 40)))
                        {
                            stackingChanged = true;
                        }

                        break;
                    default:
                        if (_randrEventBase >= 0 &&
                            (ev.Type == _randrEventBase + X11.RRScreenChangeNotify || ev.Type == _randrEventBase + X11.RRNotify))
                        {
                            displaysChanged = true;
                        }

                        break;
                }
            }

            if (displaysChanged)
            {
                DisplaysChanged?.Invoke();
            }

            if (stackingChanged)
            {
                StackingChanged?.Invoke();
            }

            fd.Revents = 0;
            poll(&fd, 1, 250);
        }

        X11.XCloseDisplay(_display);
    }

    private void OnKeyPress(uint keycode, uint state)
    {
        const uint wanted = X11.ControlMask | X11.Mod1Mask;
        var modifiers = state & (X11.ShiftMask | X11.ControlMask | X11.Mod1Mask | X11.Mod4Mask);
        if (modifiers != wanted || !_grabbed.TryGetValue((int)keycode, out var action))
        {
            return;
        }

        // Held keys auto-repeat on X11. Repeating stronger/weaker is fine; toggling is not.
        if (action == HotkeyAction.ToggleTint)
        {
            var now = Environment.TickCount64;
            if (now - _lastTogglePress < 400)
            {
                _lastTogglePress = now;
                return;
            }

            _lastTogglePress = now;
        }

        HotkeyPressed?.Invoke(action);
    }

    private const short PollIn = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int poll(PollFd* fds, nuint count, int timeout);
}
