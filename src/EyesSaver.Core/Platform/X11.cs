using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace EyesSaver.Core.Platform;

/// <summary>
/// The parts of Xlib, XRandR and XShape that Eyes Saver uses on Linux. XIDs (windows, atoms,
/// outputs, CRTCs) are unsigned long, which is 64-bit on Linux x64, so they are passed as nint.
/// </summary>
public static unsafe class X11
{
    private const string LibX11 = "libX11.so.6";
    private const string LibXrandr = "libXrandr.so.2";
    private const string LibXext = "libXext.so.6";

    public const int Success = 0;
    public const int BadAccess = 10;

    public const int TrueColor = 4;
    public const int AllocNone = 0;
    public const uint InputOutput = 1;
    public const nint CWBackPixel = 1 << 1;
    public const nint CWBorderPixel = 1 << 3;
    public const nint CWOverrideRedirect = 1 << 9;
    public const nint CWColormap = 1 << 13;

    public const int PropModeReplace = 0;
    public const nint XA_ATOM = 4;
    public const nint XA_CARDINAL = 6;
    public const nint XA_STRING = 31;

    public const int ShapeSet = 0;
    public const int ShapeInput = 2;
    public const int Unsorted = 0;

    public const uint ShiftMask = 1;
    public const uint LockMask = 2;
    public const uint ControlMask = 4;
    public const uint Mod1Mask = 8;
    public const uint Mod2Mask = 16;
    public const uint Mod4Mask = 64;
    public const int GrabModeAsync = 1;

    public const int KeyPress = 2;
    public const int MapNotify = 19;
    public const int ConfigureNotify = 22;
    public const nint SubstructureNotifyMask = 1 << 19;

    public const int RRScreenChangeNotifyMask = 1;
    public const int RRCrtcChangeNotifyMask = 2;
    public const int RROutputChangeNotifyMask = 4;
    public const int RRScreenChangeNotify = 0;
    public const int RRNotify = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct XVisualInfo
    {
        public nint Visual;
        public nint VisualId;
        public int Screen;
        public int Depth;
        public int Class;
        public nint RedMask;
        public nint GreenMask;
        public nint BlueMask;
        public int ColormapSize;
        public int BitsPerRgb;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XSetWindowAttributes
    {
        public nint BackgroundPixmap;
        public nint BackgroundPixel;
        public nint BorderPixmap;
        public nint BorderPixel;
        public int BitGravity;
        public int WinGravity;
        public int BackingStore;
        public nint BackingPlanes;
        public nint BackingPixel;
        public int SaveUnder;
        public nint EventMask;
        public nint DoNotPropagateMask;
        public int OverrideRedirect;
        public nint Colormap;
        public nint Cursor;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XRRMonitorInfo
    {
        public nint Name;
        public int Primary;
        public int Automatic;
        public int NOutput;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public int MWidth;
        public int MHeight;
        public nint Outputs;
    }

    /// <summary>XEvent is a union of 24 longs.</summary>
    [StructLayout(LayoutKind.Sequential, Size = 192)]
    public struct XEvent
    {
        public int Type;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int XErrorHandler(nint display, nint errorEvent);

    [DllImport(LibX11)] public static extern nint XOpenDisplay(nint name);
    [DllImport(LibX11)] public static extern int XCloseDisplay(nint display);
    [DllImport(LibX11)] public static extern int XDefaultScreen(nint display);
    [DllImport(LibX11)] public static extern nint XRootWindow(nint display, int screen);
    [DllImport(LibX11)] public static extern int XDisplayWidth(nint display, int screen);
    [DllImport(LibX11)] public static extern int XDisplayHeight(nint display, int screen);
    [DllImport(LibX11)] public static extern int XConnectionNumber(nint display);
    [DllImport(LibX11)] public static extern nint XInternAtom(nint display, string name, int onlyIfExists);
    [DllImport(LibX11)] public static extern nint XGetAtomName(nint display, nint atom);
    [DllImport(LibX11)] public static extern nint XGetSelectionOwner(nint display, nint selection);
    [DllImport(LibX11)] public static extern nint XGetDefault(nint display, string program, string option);
    [DllImport(LibX11)] public static extern int XFree(nint data);
    [DllImport(LibX11)] public static extern int XFlush(nint display);
    [DllImport(LibX11)] public static extern int XSync(nint display, int discard);
    [DllImport(LibX11)] public static extern int XPending(nint display);
    [DllImport(LibX11)] public static extern int XNextEvent(nint display, XEvent* ev);
    [DllImport(LibX11)] public static extern nint XSetErrorHandler(nint handler);

    [DllImport(LibX11)] public static extern int XMatchVisualInfo(nint display, int screen, int depth, int visualClass, out XVisualInfo info);
    [DllImport(LibX11)] public static extern nint XCreateColormap(nint display, nint window, nint visual, int alloc);
    [DllImport(LibX11)] public static extern int XFreeColormap(nint display, nint colormap);

    [DllImport(LibX11)]
    public static extern nint XCreateWindow(
        nint display, nint parent, int x, int y, uint width, uint height, uint borderWidth,
        int depth, uint windowClass, nint visual, nint valueMask, ref XSetWindowAttributes attributes);

    [DllImport(LibX11)] public static extern int XDestroyWindow(nint display, nint window);
    [DllImport(LibX11)] public static extern int XMapRaised(nint display, nint window);
    [DllImport(LibX11)] public static extern int XRaiseWindow(nint display, nint window);
    [DllImport(LibX11)] public static extern int XMoveResizeWindow(nint display, nint window, int x, int y, uint width, uint height);
    [DllImport(LibX11)] public static extern int XSetWindowBackground(nint display, nint window, nint pixel);
    [DllImport(LibX11)] public static extern int XClearWindow(nint display, nint window);
    [DllImport(LibX11)] public static extern int XStoreName(nint display, nint window, string name);
    [DllImport(LibX11)] public static extern int XChangeProperty(nint display, nint window, nint property, nint type, int format, int mode, void* data, int count);
    [DllImport(LibX11)] public static extern int XSelectInput(nint display, nint window, nint eventMask);

    [DllImport(LibX11)]
    public static extern int XQueryPointer(
        nint display, nint window, out nint rootReturn, out nint childReturn,
        out int rootX, out int rootY, out int windowX, out int windowY, out uint mask);

    [DllImport(LibX11)] public static extern byte XKeysymToKeycode(nint display, nint keysym);
    [DllImport(LibX11)] public static extern int XGrabKey(nint display, int keycode, uint modifiers, nint grabWindow, int ownerEvents, int pointerMode, int keyboardMode);
    [DllImport(LibX11)] public static extern int XUngrabKey(nint display, int keycode, uint modifiers, nint grabWindow);

    [DllImport(LibXext)] public static extern void XShapeCombineRectangles(nint display, nint window, int destKind, int xOff, int yOff, nint rectangles, int count, int op, int ordering);

    [DllImport(LibXrandr)] public static extern int XRRQueryExtension(nint display, out int eventBase, out int errorBase);
    [DllImport(LibXrandr)] public static extern int XRRQueryVersion(nint display, out int major, out int minor);
    [DllImport(LibXrandr)] public static extern nint XRRGetMonitors(nint display, nint window, int getActive, out int count);
    [DllImport(LibXrandr)] public static extern void XRRFreeMonitors(nint monitors);
    [DllImport(LibXrandr)] public static extern void XRRSelectInput(nint display, nint window, int mask);
    [DllImport(LibXrandr)] public static extern nint XRRGetScreenResourcesCurrent(nint display, nint window);
    [DllImport(LibXrandr)] public static extern void XRRFreeScreenResources(nint resources);
    [DllImport(LibXrandr)] public static extern nint XRRGetOutputInfo(nint display, nint resources, nint output);
    [DllImport(LibXrandr)] public static extern void XRRFreeOutputInfo(nint info);
    [DllImport(LibXrandr)] public static extern int XRRGetCrtcGammaSize(nint display, nint crtc);
    [DllImport(LibXrandr)] public static extern nint XRRGetCrtcGamma(nint display, nint crtc);
    [DllImport(LibXrandr)] public static extern nint XRRAllocGamma(int size);
    [DllImport(LibXrandr)] public static extern void XRRSetCrtcGamma(nint display, nint crtc, nint gamma);
    [DllImport(LibXrandr)] public static extern void XRRFreeGamma(nint gamma);

    [DllImport(LibXrandr)]
    public static extern int XRRGetOutputProperty(
        nint display, nint output, nint property, nint offset, nint length, int delete, int pending,
        nint requestType, out nint actualType, out int actualFormat, out nint itemCount, out nint bytesAfter, out nint data);

    public static string? AtomName(nint display, nint atom)
    {
        var pointer = XGetAtomName(display, atom);
        if (pointer == 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUTF8(pointer);
        }
        finally
        {
            XFree(pointer);
        }
    }

    /// <summary>The CRTC an output is shown on, or 0 when it is off.</summary>
    public static nint CrtcForOutput(nint display, nint output)
    {
        var root = XRootWindow(display, XDefaultScreen(display));
        var resources = XRRGetScreenResourcesCurrent(display, root);
        if (resources == 0)
        {
            return 0;
        }

        try
        {
            var info = XRRGetOutputInfo(display, resources, output);
            if (info == 0)
            {
                return 0;
            }

            // XRROutputInfo: Time timestamp; RRCrtc crtc; ...
            var crtc = Marshal.ReadIntPtr(info, 8);
            XRRFreeOutputInfo(info);
            return crtc;
        }
        finally
        {
            XRRFreeScreenResources(resources);
        }
    }

    /// <summary>True when a compositing manager runs, so ARGB windows blend instead of painting opaque.</summary>
    public static bool IsComposited(nint display)
    {
        var atom = XInternAtom(display, $"_NET_WM_CM_S{XDefaultScreen(display)}", 0);
        return atom != 0 && XGetSelectionOwner(display, atom) != 0;
    }

    public static void SetAtomProperty(nint display, nint window, string property, string value)
    {
        var name = XInternAtom(display, property, 0);
        var atom = XInternAtom(display, value, 0);
        XChangeProperty(display, window, name, XA_ATOM, 32, PropModeReplace, &atom, 1);
    }

    public static void SetCardinalProperty(nint display, nint window, string property, long value)
    {
        var name = XInternAtom(display, property, 0);
        var data = (nint)value;
        XChangeProperty(display, window, name, XA_CARDINAL, 32, PropModeReplace, &data, 1);
    }

    /// <summary>Makes the window ignore all mouse input, so clicks reach whatever is below it.</summary>
    public static void MakeClickThrough(nint display, nint window)
    {
        XShapeCombineRectangles(display, window, ShapeInput, 0, 0, 0, 0, ShapeSet, Unsorted);
    }
}

/// <summary>
/// Owns the Xlib connections Eyes Saver opens itself (separate from Avalonia's) and an
/// error handler for them. Xlib's default handler exits the process on any error, which an
/// unavailable hotkey (BadAccess) would trigger.
/// </summary>
public static unsafe class X11Session
{
    private static readonly ConcurrentDictionary<nint, int> LastErrors = new();
    private static readonly object Gate = new();
    private static X11.XErrorHandler? _handler;
    private static nint _previous;
    private static nint _shared;

    /// <summary>Connection for the UI thread. Opened on first use; 0 when there is no X server.</summary>
    public static nint Shared
    {
        get
        {
            lock (Gate)
            {
                if (_shared == 0)
                {
                    _shared = Open();
                }

                return _shared;
            }
        }
    }

    public static bool IsAvailable => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) && Shared != 0;

    public static nint Open()
    {
        nint display;
        try
        {
            display = X11.XOpenDisplay(0);
        }
        catch (DllNotFoundException)
        {
            return 0;
        }

        if (display != 0)
        {
            InstallHandler();
            LastErrors[display] = 0;
        }

        return display;
    }

    /// <summary>Waits for the server to process every request and returns the last error code (0 for none).</summary>
    public static int SyncAndTakeError(nint display)
    {
        X11.XSync(display, 0);
        return LastErrors.TryGetValue(display, out var code) && LastErrors.TryUpdate(display, 0, code) ? code : 0;
    }

    private static void InstallHandler()
    {
        lock (Gate)
        {
            if (_handler is not null)
            {
                return;
            }

            _handler = OnError;

            // Chain to the previous handler (Avalonia's) for connections that are not ours.
            _previous = X11.XSetErrorHandler(Marshal.GetFunctionPointerForDelegate(_handler));
        }
    }

    private static int OnError(nint display, nint errorEvent)
    {
        if (LastErrors.ContainsKey(display))
        {
            // XErrorEvent: int type; Display* display; XID resourceid; unsigned long serial; unsigned char error_code; ...
            LastErrors[display] = Marshal.ReadByte(errorEvent, 32);
            return 0;
        }

        return _previous != 0
            ? ((delegate* unmanaged[Cdecl]<nint, nint, int>)_previous)(display, errorEvent)
            : 0;
    }
}
