using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fetih.Patch;
using static Fetih.Launcher.Native;

namespace Fetih.Launcher;

// The old Play screen: news on the parchment, progress with Oyna and Çık bottom right, Ayarlar
// top right. All drawn with GDI on the 800x600 Main.bmp.
sealed unsafe class LauncherWindow
{
    const int Width = 800, Height = 600;
    const uint WM_SYNCED = WM_APP + 1;

    static readonly RECT NewsArea = new(62, 116, 574, 504);
    static readonly RECT PlayButton = new(578, 440, 578 + 210, 440 + 80);
    static readonly RECT ExitButton = new(629, 526, 629 + 108, 526 + 30);
    static readonly RECT SettingsButton = new(650, 34, 650 + 108, 34 + 30);
    static readonly RECT StatusArea = new(596, 362, 784, 396);
    static readonly RECT Bar = new(606, 402, 774, 412);

    static readonly uint Ink = Rgb(74, 44, 16), Faded = Rgb(138, 98, 48), Hot = Rgb(150, 40, 20);
    static readonly uint Gold = Rgb(205, 182, 92), GoldHot = Rgb(239, 207, 99);

    static LauncherWindow? _current;

    readonly Updater _updater;
    readonly List<(RECT Rect, Action Click)> _hot = [];
    nint _hwnd, _main, _region, _play, _playHot, _playDown, _exit, _exitHot, _exitDown;
    nint _newsFont, _newsDateFont, _smallFont, _buttonFont, _titleFont;
    int _hover = -1, _pressed = -1;
    bool _tracking;

    // Ayarlar, while open
    bool _settingsOpen;
    ScreenMode _editing;

    LauncherWindow(Updater updater) => _updater = updater;

    public static int Run(Updater updater)
    {
        var window = new LauncherWindow(updater);
        _current = window;
        return window.Loop();
    }

    int Loop()
    {
        var instance = GetModuleHandle(0);
        fixed (char* className = "FetihLauncher")
        {
            var wc = new WNDCLASSEXW
            {
                Size = (uint)sizeof(WNDCLASSEXW),
                WndProc = &WndProc,
                Instance = instance,
                Icon = LoadIcon(instance, 32512),
                Cursor = LoadCursor(0, IDC_ARROW),
                ClassName = className,
            };
            RegisterClassEx(&wc);
        }

        this.LoadArt();
        var x = (GetSystemMetrics(SM_CXSCREEN) - Width) / 2;
        var y = (GetSystemMetrics(SM_CYSCREEN) - Height) / 2;
        _hwnd = CreateWindowEx(WS_EX_APPWINDOW, "FetihLauncher", Install.Name, WS_POPUP | WS_MINIMIZEBOX | WS_SYSMENU,
            x, y, Width, Height, 0, 0, instance, 0);
        if (_region != 0)
            SetWindowRgn(_hwnd, _region, 0);
        ShowWindow(_hwnd, SW_SHOW);
        SetForegroundWindow(_hwnd);
        SetTimer(_hwnd, 1, 100, 0);

        _updater.Changed = () => PostMessage(_hwnd, WM_SYNCED, 0, 0);
        _updater.Owner = _hwnd;
        _updater.Start();

        MSG msg;
        while (GetMessage(&msg, 0, 0, 0) > 0)
        {
            TranslateMessage(&msg);
            DispatchMessage(&msg);
        }
        return 0;
    }

    void LoadArt()
    {
        _main = Art.Bitmap("Main.bmp");
        _region = Art.Region("Main.rgn");
        _play = Art.Bitmap("high01.bmp");
        _playHot = Art.Bitmap("high02.bmp");
        _playDown = Art.Bitmap("high03.bmp");
        _exit = Art.Bitmap("Exit01.bmp");
        _exitHot = Art.Bitmap("Exit02.bmp");
        _exitDown = Art.Bitmap("Exit03.bmp");
        _titleFont = CreateFont(-20, 0, 0, 0, FW_BOLD, 0, 0, 0, DEFAULT_CHARSET, 0, 0, CLEARTYPE_QUALITY, 0, "Palatino Linotype");
        _newsFont = CreateFont(-17, 0, 0, 0, FW_NORMAL, 0, 0, 0, DEFAULT_CHARSET, 0, 0, CLEARTYPE_QUALITY, 0, "Palatino Linotype");
        _newsDateFont = CreateFont(-14, 0, 0, 0, FW_NORMAL, 1, 0, 0, DEFAULT_CHARSET, 0, 0, CLEARTYPE_QUALITY, 0, "Palatino Linotype");
        _smallFont = CreateFont(-13, 0, 0, 0, FW_NORMAL, 0, 0, 0, DEFAULT_CHARSET, 0, 0, CLEARTYPE_QUALITY, 0, "Tahoma");
        _buttonFont = CreateFont(-14, 0, 0, 0, FW_BOLD, 0, 0, 0, DEFAULT_CHARSET, 0, 0, CLEARTYPE_QUALITY, 0, "Tahoma");
    }

    [UnmanagedCallersOnly]
    static nint WndProc(nint hwnd, uint msg, nint w, nint l)
    {
        var self = _current;
        if (self == null || (self._hwnd != 0 && hwnd != self._hwnd))
            return DefWindowProc(hwnd, msg, w, l);
        try
        {
            return self.Handle(hwnd, msg, w, l);
        }
        catch (Exception ex)
        {
            MessageBox(hwnd, ex.ToString(), Install.Name, MB_ICONERROR);
            return 0;
        }
    }

    nint Handle(nint hwnd, uint msg, nint w, nint l)
    {
        switch (msg)
        {
            case WM_ERASEBKGND:
                return 1;
            case WM_PAINT:
                this.Paint(hwnd);
                return 0;
            case WM_TIMER:
                if (_updater.Phase == Phase.Updating)
                    InvalidateRect(hwnd, 0, 0);
                return 0;
            case WM_SYNCED:
                if (_updater.Phase == Phase.Closing)
                    DestroyWindow(hwnd);
                else
                    InvalidateRect(hwnd, 0, 0);
                return 0;
            case WM_NCHITTEST:
            {
                var pt = new POINT { X = LowWord(l), Y = HighWord(l) };
                ScreenToClient(hwnd, &pt);
                return this.HitTest(pt.X, pt.Y) >= 0 ? HTCLIENT : HTCAPTION;
            }
            case WM_SETCURSOR:
                if (LowWord(l) == HTCLIENT)
                {
                    SetCursor(LoadCursor(0, IDC_HAND));
                    return 1;
                }
                break;
            case WM_MOUSEMOVE:
            {
                if (!_tracking)
                {
                    var tme = new TRACKMOUSEEVENT { Size = (uint)sizeof(TRACKMOUSEEVENT), Flags = TME_LEAVE, Hwnd = hwnd };
                    TrackMouseEvent(&tme);
                    _tracking = true;
                }
                this.SetHover(this.HitTest(LowWord(l), HighWord(l)));
                return 0;
            }
            case WM_MOUSELEAVE:
                _tracking = false;
                this.SetHover(-1);
                return 0;
            case WM_LBUTTONDOWN:
                _pressed = _hover;
                SetCapture(hwnd);
                InvalidateRect(hwnd, 0, 0);
                return 0;
            case WM_LBUTTONUP:
            {
                ReleaseCapture();
                var clicked = _pressed >= 0 && _pressed == this.HitTest(LowWord(l), HighWord(l)) ? _hot[_pressed].Click : null;
                _pressed = -1;
                InvalidateRect(hwnd, 0, 0);
                clicked?.Invoke();
                return 0;
            }
            case WM_CLOSE:
                DestroyWindow(hwnd);
                return 0;
            case WM_DESTROY:
                _updater.Stop();
                PostQuitMessage(0);
                return 0;
        }
        return DefWindowProc(hwnd, msg, w, l);
    }

    int HitTest(int x, int y)
    {
        for (var i = 0; i < _hot.Count; i++)
            if (_hot[i].Rect.Contains(x, y))
                return i;
        return -1;
    }

    void SetHover(int index)
    {
        if (index == _hover)
            return;
        _hover = index;
        InvalidateRect(_hwnd, 0, 0);
    }

    void Paint(nint hwnd)
    {
        PAINTSTRUCT ps;
        var hdc = BeginPaint(hwnd, &ps);
        var buffer = CreateCompatibleDC(hdc);
        var surface = CreateCompatibleBitmap(hdc, Width, Height);
        var oldSurface = SelectObject(buffer, surface);
        var art = CreateCompatibleDC(hdc);
        try
        {
            this.Draw(buffer, art);
            BitBlt(hdc, 0, 0, Width, Height, buffer, 0, 0, SRCCOPY);
        }
        finally
        {
            DeleteDC(art);
            SelectObject(buffer, oldSurface);
            DeleteObject(surface);
            DeleteDC(buffer);
            EndPaint(hwnd, &ps);
        }
    }

    void Draw(nint dc, nint art)
    {
        var previousHot = _hot.Count;
        _hot.Clear();
        SetBkMode(dc, TRANSPARENT);

        if (_main != 0)
        {
            var old = SelectObject(art, _main);
            BitBlt(dc, 0, 0, Width, Height, art, 0, 0, SRCCOPY);
            SelectObject(art, old);
        }
        else
        {
            var all = new RECT(0, 0, Width, Height);
            var brush = CreateSolidBrush(Rgb(222, 190, 130));
            FillRect(dc, &all, brush);
            DeleteObject(brush);
        }

        if (_settingsOpen)
            this.DrawSettings(dc);
        else
            this.DrawNews(dc);

        this.DrawStatus(dc);

        var ready = _updater.Phase is Phase.Ready or Phase.Failed;
        this.ImageButton(dc, art, PlayButton, _play, _playHot, _playDown, transparent: true, enabled: ready,
            _updater.Phase == Phase.Failed ? _updater.Retry : this.PlayClicked);
        this.ImageButton(dc, art, ExitButton, _exit, _exitHot, _exitDown, transparent: false, enabled: true,
            () => DestroyWindow(_hwnd));
        if (_updater.Phase == Phase.Ready)
            this.SettingsButtonDraw(dc, art);

        // The hot list changed under the mouse: hover again on the next move
        if (_hot.Count != previousHot)
            _hover = -1;
    }

    void DrawNews(nint dc)
    {
        var news = _updater.News;
        var area = NewsArea;
        if (news == null)
        {
            this.Text(dc, _newsDateFont, Faded, _updater.NewsFailed ? "Haberler alınamadı." : "Haberler yükleniyor...", area, DT_LEFT);
            return;
        }
        var y = area.Top;
        foreach (var (date, subject) in news)
        {
            if (y + 44 > area.Bottom)
                break;
            this.Text(dc, _newsDateFont, Faded, date, new RECT(area.Left, y, area.Right, y + 18), DT_LEFT | DT_SINGLELINE);
            var text = new RECT(area.Left, y + 17, area.Right, area.Bottom);
            SelectObject(dc, _newsFont);
            DrawText(dc, subject, -1, &text, DT_LEFT | DT_WORDBREAK | DT_NOPREFIX | DT_CALCRECT);
            var height = Math.Min(text.Bottom - text.Top, 44);
            this.Text(dc, _newsFont, Ink, subject, new RECT(area.Left, y + 17, area.Right, y + 17 + height), DT_LEFT | DT_WORDBREAK | DT_END_ELLIPSIS);
            y += 17 + height + 12;
        }
    }

    void DrawSettings(nint dc)
    {
        var left = NewsArea.Left;
        var y = NewsArea.Top;
        this.Text(dc, _titleFont, Ink, "Ayarlar", new RECT(left, y, NewsArea.Right, y + 26), DT_LEFT | DT_SINGLELINE);
        y += 40;

        this.Text(dc, _newsFont, Faded, "Ekran", new RECT(left, y, NewsArea.Right, y + 22), DT_LEFT | DT_SINGLELINE);
        y += 26;
        this.Choice(dc, new RECT(left + 16, y, left + 176, y + 22), "Tam ekran", _editing.FullScreen,
            () => _editing = _editing with { FullScreen = true });
        this.Choice(dc, new RECT(left + 186, y, left + 346, y + 22), "Pencere", !_editing.FullScreen,
            () => _editing = _editing with { FullScreen = false });
        y += 38;

        this.Text(dc, _newsFont, Faded, "Boyut", new RECT(left, y, NewsArea.Right, y + 22), DT_LEFT | DT_SINGLELINE);
        y += 26;
        var (screenW, screenH) = (GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));
        var sizes = GameSetUp.Sizes(screenW, screenH);
        if (!sizes.Contains((_editing.Width, _editing.Height)))
            sizes = [.. sizes, (_editing.Width, _editing.Height)];
        for (var i = 0; i < sizes.Count; i++)
        {
            var (w, h) = sizes[i];
            var column = i % 2;
            var row = i / 2;
            var rect = new RECT(left + 16 + column * 250, y + row * 26, left + 16 + column * 250 + 240, y + row * 26 + 22);
            var label = i == 0 ? $"{w} × {h} (ekranın)" : $"{w} × {h}";
            this.Choice(dc, rect, label, w == _editing.Width && h == _editing.Height,
                () => _editing = _editing with { Width = w, Height = h });
        }

        var bottom = NewsArea.Bottom - 26;
        this.Link(dc, new RECT(left, bottom, left + 90, bottom + 22), "Kaydet", this.SaveSettings);
        this.Link(dc, new RECT(left + 100, bottom, left + 190, bottom + 22), "Vazgeç", () => _settingsOpen = false);
        this.Link(dc, new RECT(NewsArea.Right - 150, bottom, NewsArea.Right, bottom + 22), "Dosyaları onar", () =>
        {
            _settingsOpen = false;
            _updater.Repair();
        });
    }

    void Choice(nint dc, RECT rect, string label, bool selected, Action click) =>
        this.Link(dc, rect, (selected ? "● " : "○ ") + label, click, selected ? Ink : Faded);

    void Link(nint dc, RECT rect, string label, Action click, uint? color = null)
    {
        var index = _hot.Count;
        _hot.Add((rect, () =>
        {
            click();
            InvalidateRect(_hwnd, 0, 0);
        }));
        this.Text(dc, _newsFont, index == _hover ? Hot : color ?? Ink, label, rect, DT_LEFT | DT_SINGLELINE | DT_VCENTER);
    }

    void DrawStatus(nint dc)
    {
        var status = _updater.Status;
        this.Text(dc, _smallFont, Gold, status, StatusArea, DT_CENTER | DT_WORDBREAK);
        if (_updater.Phase != Phase.Updating || _updater.Total <= 0)
            return;

        var frame = new RECT(Bar.Left - 1, Bar.Top - 1, Bar.Right + 1, Bar.Bottom + 1);
        var border = CreateSolidBrush(Rgb(120, 96, 48));
        var back = CreateSolidBrush(Rgb(28, 22, 14));
        var fill = CreateSolidBrush(Rgb(214, 168, 64));
        FillRect(dc, &frame, border);
        var inner = Bar;
        FillRect(dc, &inner, back);
        var done = Math.Clamp((double)_updater.Done / _updater.Total, 0, 1);
        var bar = new RECT(Bar.Left, Bar.Top, Bar.Left + (int)((Bar.Right - Bar.Left) * done), Bar.Bottom);
        FillRect(dc, &bar, fill);
        DeleteObject(border);
        DeleteObject(back);
        DeleteObject(fill);
    }

    void ImageButton(nint dc, nint art, RECT rect, nint normal, nint hot, nint down, bool transparent, bool enabled, Action click)
    {
        var index = -1;
        if (enabled)
        {
            index = _hot.Count;
            _hot.Add((rect, click));
        }
        var image = index >= 0 && index == _pressed && index == _hover ? down : index >= 0 && index == _hover ? hot : normal;
        if (image == 0)
            return;
        var old = SelectObject(art, image);
        var (w, h) = (rect.Right - rect.Left, rect.Bottom - rect.Top);
        if (transparent)
            TransparentBlt(dc, rect.Left, rect.Top, w, h, art, 0, 0, w, h, Art.Key);
        else
            BitBlt(dc, rect.Left, rect.Top, w, h, art, 0, 0, SRCCOPY);
        SelectObject(art, old);
    }

    // The Çık frame with its text tiled over by the plain texture left of it, and our own text
    void SettingsButtonDraw(nint dc, nint art)
    {
        var rect = SettingsButton;
        var index = _hot.Count;
        _hot.Add((rect, this.OpenSettings));
        var image = index == _pressed && index == _hover ? _exitDown : index == _hover ? _exitHot : _exit;
        if (image == 0)
            return;
        var old = SelectObject(art, image);
        BitBlt(dc, rect.Left, rect.Top, 108, 30, art, 0, 0, SRCCOPY);
        for (var x = 36; x < 80; x += 28)
            BitBlt(dc, rect.Left + x, rect.Top + 5, Math.Min(28, 80 - x), 20, art, 8, 5, SRCCOPY);
        SelectObject(art, old);
        this.Text(dc, _buttonFont, index == _hover ? GoldHot : Gold, "Ayarlar",
            new RECT(rect.Left, rect.Top + 4, rect.Right, rect.Bottom - 4), DT_CENTER | DT_VCENTER | DT_SINGLELINE);
    }

    void OpenSettings()
    {
        _settingsOpen = !_settingsOpen;
        if (_settingsOpen)
            _editing = _updater.ReadScreen();
        InvalidateRect(_hwnd, 0, 0);
    }

    void SaveSettings()
    {
        _updater.WriteScreen(_editing);
        _settingsOpen = false;
    }

    void PlayClicked()
    {
        if (_updater.StartGame())
            DestroyWindow(_hwnd);
    }

    void Text(nint dc, nint font, uint color, string text, RECT rect, uint format)
    {
        SelectObject(dc, font);
        SetTextColor(dc, color);
        DrawText(dc, text, -1, &rect, format | DT_NOPREFIX);
    }
}
