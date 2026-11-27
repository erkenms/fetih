using System.Runtime.InteropServices;

namespace Fetih.Launcher;

static unsafe partial class Native
{
    public const uint WS_POPUP = 0x80000000, WS_MINIMIZEBOX = 0x00020000, WS_SYSMENU = 0x00080000;
    public const uint WS_EX_APPWINDOW = 0x00040000;
    public const uint WM_DESTROY = 0x0002, WM_PAINT = 0x000F, WM_CLOSE = 0x0010, WM_ERASEBKGND = 0x0014,
        WM_SETCURSOR = 0x0020, WM_NCHITTEST = 0x0084, WM_TIMER = 0x0113, WM_MOUSEMOVE = 0x0200,
        WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_MOUSELEAVE = 0x02A3, WM_APP = 0x8000;
    public const int HTCLIENT = 1, HTCAPTION = 2;
    public const int SM_CXSCREEN = 0, SM_CYSCREEN = 1;
    public const int SW_SHOW = 5;
    public const uint SRCCOPY = 0x00CC0020;
    public const int TRANSPARENT = 1;
    public const uint DT_LEFT = 0, DT_CENTER = 1, DT_VCENTER = 4, DT_WORDBREAK = 0x10, DT_SINGLELINE = 0x20,
        DT_NOPREFIX = 0x800, DT_END_ELLIPSIS = 0x8000, DT_CALCRECT = 0x400;
    public const uint MB_OK = 0, MB_OKCANCEL = 1, MB_YESNO = 4, MB_RETRYCANCEL = 5, MB_ICONERROR = 0x10,
        MB_ICONWARNING = 0x30, MB_ICONINFORMATION = 0x40;
    public const int IDOK = 1, IDRETRY = 4, IDYES = 6;
    public const uint TME_LEAVE = 2;
    public const int IDC_ARROW = 32512, IDC_HAND = 32649;
    public const int FW_NORMAL = 400, FW_BOLD = 700;
    public const uint CLEARTYPE_QUALITY = 5, DEFAULT_CHARSET = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT(int left, int top, int right, int bottom)
    {
        public int Left = left, Top = top, Right = right, Bottom = bottom;
        public readonly bool Contains(int x, int y) => x >= this.Left && x < this.Right && y >= this.Top && y < this.Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam, LParam;
        public uint Time;
        public POINT Pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEXW
    {
        public uint Size, Style;
        public delegate* unmanaged<nint, uint, nint, nint, nint> WndProc;
        public int ClsExtra, WndExtra;
        public nint Instance, Icon, Cursor, Background;
        public char* MenuName, ClassName;
        public nint IconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public nint Hdc;
        public int Erase;
        public RECT Paint;
        public int Restore, IncUpdate;
        public fixed byte Reserved[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TRACKMOUSEEVENT
    {
        public uint Size, Flags;
        public nint Hwnd;
        public uint HoverTime;
    }

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW")]
    public static partial ushort RegisterClassEx(WNDCLASSEXW* wc);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowEx(uint exStyle, string className, string title, uint style,
        int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    public static partial nint DefWindowProc(nint hwnd, uint msg, nint w, nint l);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    public static partial int GetMessage(MSG* msg, nint hwnd, uint min, uint max);

    [LibraryImport("user32.dll")]
    public static partial int TranslateMessage(MSG* msg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    public static partial nint DispatchMessage(MSG* msg);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    public static partial int PostMessage(nint hwnd, uint msg, nint w, nint l);

    [LibraryImport("user32.dll")]
    public static partial void PostQuitMessage(int code);

    [LibraryImport("user32.dll")]
    public static partial int ShowWindow(nint hwnd, int cmd);

    [LibraryImport("user32.dll")]
    public static partial int DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial int SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial int InvalidateRect(nint hwnd, nint rect, int erase);

    [LibraryImport("user32.dll")]
    public static partial nint BeginPaint(nint hwnd, PAINTSTRUCT* ps);

    [LibraryImport("user32.dll")]
    public static partial int EndPaint(nint hwnd, PAINTSTRUCT* ps);

    [LibraryImport("user32.dll")]
    public static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial int ReleaseDC(nint hwnd, nint hdc);

    [LibraryImport("user32.dll")]
    public static partial nuint SetTimer(nint hwnd, nuint id, uint ms, nint proc);

    [LibraryImport("user32.dll")]
    public static partial int SetWindowRgn(nint hwnd, nint rgn, int redraw);

    [LibraryImport("user32.dll")]
    public static partial int ScreenToClient(nint hwnd, POINT* pt);

    [LibraryImport("user32.dll")]
    public static partial int TrackMouseEvent(TRACKMOUSEEVENT* tme);

    [LibraryImport("user32.dll")]
    public static partial nint SetCapture(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial int ReleaseCapture();

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")]
    public static partial nint LoadCursor(nint instance, nint name);

    [LibraryImport("user32.dll")]
    public static partial nint SetCursor(nint cursor);

    [LibraryImport("user32.dll", EntryPoint = "LoadIconW")]
    public static partial nint LoadIcon(nint instance, nint name);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MessageBox(nint hwnd, string text, string caption, uint type);

    [LibraryImport("user32.dll", EntryPoint = "DrawTextW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int DrawText(nint hdc, string text, int count, RECT* rect, uint format);

    [LibraryImport("user32.dll")]
    public static partial int FillRect(nint hdc, RECT* rect, nint brush);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    public static partial nint GetModuleHandle(nint name);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateCompatibleBitmap(nint hdc, int w, int h);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateDIBSection(nint hdc, BITMAPINFOHEADER* info, uint usage, void** bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    public static partial nint SelectObject(nint hdc, nint obj);

    [LibraryImport("gdi32.dll")]
    public static partial int DeleteObject(nint obj);

    [LibraryImport("gdi32.dll")]
    public static partial int DeleteDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    public static partial int BitBlt(nint dest, int x, int y, int w, int h, nint src, int sx, int sy, uint rop);

    [LibraryImport("gdi32.dll")]
    public static partial int StretchBlt(nint dest, int x, int y, int w, int h, nint src, int sx, int sy, int sw, int sh, uint rop);

    // BLENDFUNCTION { AC_SRC_OVER, 0, 255, AC_SRC_ALPHA } as one DWORD
    public const uint PremultipliedOver = 0x01FF0000;

    [LibraryImport("msimg32.dll")]
    public static partial int AlphaBlend(nint dest, int x, int y, int w, int h, nint src, int sx, int sy, int sw, int sh, uint blend);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForSystem();

    [LibraryImport("gdi32.dll")]
    public static partial nint ExtCreateRegion(nint xform, uint count, void* data);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateSolidBrush(uint color);

    [LibraryImport("gdi32.dll")]
    public static partial int SetBkMode(nint hdc, int mode);

    [LibraryImport("gdi32.dll")]
    public static partial uint SetTextColor(nint hdc, uint color);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateFontW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateFont(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision,
        uint quality, uint pitchAndFamily, string face);

    [LibraryImport("ole32.dll")]
    public static partial int CoInitializeEx(nint reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    public static partial int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid, nint* obj);

    // The screen as Conquer.exe sees it: it isn't DPI-aware, so Windows hands it the size
    // divided by the scale (2560 at 150% → 1707)
    public static (int Width, int Height) GameScreen()
    {
        var dpi = GetDpiForSystem();
        return ((int)Math.Round(GetSystemMetrics(SM_CXSCREEN) * 96.0 / dpi), (int)Math.Round(GetSystemMetrics(SM_CYSCREEN) * 96.0 / dpi));
    }

    public static uint Rgb(int r, int g, int b) => (uint)(r | (g << 8) | (b << 16));

    public static int LowWord(nint l) => (short)(l & 0xFFFF);

    public static int HighWord(nint l) => (short)((l >> 16) & 0xFFFF);
}
