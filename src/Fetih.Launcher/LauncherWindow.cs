using System.Runtime.InteropServices;
using Fetih.Patch;
using static Fetih.Launcher.Native;

namespace Fetih.Launcher;

// The old Play screen: news on the parchment, progress with Oyna and Ayarlar bottom right,
// minimize and close top right. Laid out in the art's 800x600 and drawn at the screen's DPI: the art scaled once,
// text at full resolution.
sealed unsafe class LauncherWindow
{
    const int ArtWidth = 800, ArtHeight = 600;
    const uint WM_SYNCED = WM_APP + 1;

    const int CharacterX = 594;
    static readonly RECT NewsArea = new(62, 116, 574, 504);
    static readonly RECT PlayButton = new(584, 448, 584 + 210, 448 + 80);
    static readonly RECT SettingsButton = new(635, 532, 635 + 108, 532 + 30);
    // The 15x20 glyphs at 1.3x
    static readonly RECT MinimizeButton = new(734, 11, 734 + 20, 11 + 26);
    static readonly RECT CloseButton = new(756, 11, 756 + 20, 11 + 26);
    static readonly RECT StatusArea = new(608, 360, 778, 400);
    static readonly RECT Bar = new(618, 404, 768, 409);
    static readonly RECT Amount = new(608, 413, 778, 431);

    static readonly uint Ink = Rgb(74, 44, 16), Faded = Rgb(132, 94, 46), Hot = Rgb(150, 40, 20);
    static readonly uint Gold = Rgb(205, 182, 92), GoldHot = Rgb(239, 207, 99);

    static LauncherWindow? _current;

    readonly Updater _updater;
    readonly double _scale;
    readonly int _width, _height;
    readonly List<(RECT Rect, Action Click)> _hot = [];
    nint _hwnd, _region;
    nint _main, _character;
    readonly nint[] _play = new nint[3], _settings = new nint[3];
    // Normal, hot, shadow
    readonly nint[] _minimize = new nint[3], _close = new nint[3];
    nint _newsFont, _newsDateFont, _smallFont, _buttonFont, _titleFont;
    int _hover = -1, _pressed = -1;
    bool _tracking;

    // Ayarlar, while open
    bool _settingsOpen;
    ScreenMode _editing;

    LauncherWindow(Updater updater)
    {
        _updater = updater;
        _scale = GetDpiForSystem() / 96.0;
        _width = this.S(ArtWidth);
        _height = this.S(ArtHeight);
    }

    public static int Run(Updater updater)
    {
        var window = new LauncherWindow(updater);
        _current = window;
        return window.Loop();
    }

    int S(int v) => (int)Math.Round(v * _scale);

    RECT S(RECT r) => new(this.S(r.Left), this.S(r.Top), this.S(r.Right), this.S(r.Bottom));

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
        var x = (GetSystemMetrics(SM_CXSCREEN) - _width) / 2;
        var y = (GetSystemMetrics(SM_CYSCREEN) - _height) / 2;
        _hwnd = CreateWindowEx(WS_EX_APPWINDOW, "FetihLauncher", Install.Name, WS_POPUP | WS_MINIMIZEBOX | WS_SYSMENU,
            x, y, _width, _height, 0, 0, instance, 0);
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
        _main = Art.Parchment()?.Scale(_scale).ToBitmap() ?? 0;
        _character = Art.Character()?.Scale(_scale).ToBitmap() ?? 0;
        _region = Art.Region("Main.rgn", (float)_scale);
        for (var i = 0; i < 3; i++)
        {
            _play[i] = Art.Button($"high0{i + 1}.bmp")?.Scale(_scale).ToBitmap() ?? 0;
            var exit = Art.Button($"Exit0{i + 1}.bmp");
            if (exit != null)
            {
                // The Çık frame with its text covered by the plain texture left of it
                var blank = exit.Copy();
                for (var x = 36; x < 80; x += 28)
                    blank.CopyBlock(8, 5, Math.Min(28, 80 - x), 20, x, 5);
                _settings[i] = blank.Scale(_scale).ToBitmap();
            }
        }
        uint[] glyphColors = [0xCDB65C, 0xEFCF63, 0x000000];
        for (var i = 0; i < 3; i++)
        {
            _minimize[i] = Art.Glyph("TMin.bmp", glyphColors[i])?.ScaleTo(this.S(20), this.S(26)).ToBitmap() ?? 0;
            _close[i] = Art.Glyph("TClose.bmp", glyphColors[i])?.ScaleTo(this.S(20), this.S(26)).ToBitmap() ?? 0;
        }
        _titleFont = this.Font(20, FW_BOLD, false, "Palatino Linotype");
        _newsFont = this.Font(17, FW_NORMAL, false, "Palatino Linotype");
        _newsDateFont = this.Font(13, FW_NORMAL, false, "Palatino Linotype");
        _smallFont = this.Font(13, FW_NORMAL, false, "Tahoma");
        _buttonFont = this.Font(14, FW_BOLD, false, "Tahoma");
    }

    nint Font(int pixels, int weight, bool italic, string face) =>
        CreateFont(-this.S(pixels), 0, 0, 0, weight, italic ? 1u : 0, 0, 0, DEFAULT_CHARSET, 0, 0, CLEARTYPE_QUALITY, 0, face);

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
        var surface = CreateCompatibleBitmap(hdc, _width, _height);
        var oldSurface = SelectObject(buffer, surface);
        var art = CreateCompatibleDC(hdc);
        try
        {
            this.Draw(buffer, art);
            BitBlt(hdc, 0, 0, _width, _height, buffer, 0, 0, SRCCOPY);
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

        var all = new RECT(0, 0, _width, _height);
        var background = CreateSolidBrush(_main != 0 ? Rgb(24, 16, 8) : Rgb(222, 190, 130));
        FillRect(dc, &all, background);
        DeleteObject(background);
        this.Image(dc, art, _main, 0, 0, _width, _height);
        this.Image(dc, art, _character, this.S(CharacterX), 0, _width - this.S(CharacterX), _height);

        if (_settingsOpen)
            this.DrawSettings(dc);
        else
            this.DrawNews(dc);

        this.DrawStatus(dc);

        var ready = _updater.Phase is Phase.Ready or Phase.Failed;
        this.ImageButton(dc, art, PlayButton, _play, ready, _updater.Phase == Phase.Failed ? _updater.Retry : this.PlayClicked);
        this.TitleButton(dc, art, MinimizeButton, _minimize, () => ShowWindow(_hwnd, SW_MINIMIZE));
        this.TitleButton(dc, art, CloseButton, _close, () => DestroyWindow(_hwnd));
        if (_updater.Phase == Phase.Ready)
        {
            var index = this.ImageButton(dc, art, SettingsButton, _settings, true, this.OpenSettings);
            this.Text(dc, _buttonFont, index == _hover ? GoldHot : Gold, "Ayarlar",
                new RECT(SettingsButton.Left, SettingsButton.Top + 4, SettingsButton.Right, SettingsButton.Bottom - 4),
                DT_CENTER | DT_VCENTER | DT_SINGLELINE);
        }

        // The hot list changed under the mouse: hover again on the next move
        if (_hot.Count != previousHot)
            _hover = -1;
    }

    void DrawNews(nint dc)
    {
        var news = _updater.News;
        if (news == null)
        {
            this.Text(dc, _newsDateFont, Faded, _updater.NewsFailed ? "Haberler alınamadı." : "Haberler yükleniyor...", NewsArea, DT_LEFT);
            return;
        }
        var y = NewsArea.Top;
        foreach (var (date, subject) in news)
        {
            // Measured in pixels, laid out in art units
            var measure = this.S(new RECT(NewsArea.Left, 0, NewsArea.Right, 1000));
            SelectObject(dc, _newsFont);
            DrawText(dc, subject, -1, &measure, DT_LEFT | DT_WORDBREAK | DT_NOPREFIX | DT_CALCRECT);
            var height = Math.Min((int)Math.Ceiling((measure.Bottom - measure.Top) / _scale), 44);
            if (y + 16 + height > NewsArea.Bottom)
                break;
            this.Text(dc, _newsDateFont, Faded, date, new RECT(NewsArea.Left, y, NewsArea.Right, y + 16), DT_LEFT | DT_SINGLELINE);
            this.Text(dc, _newsFont, Ink, subject, new RECT(NewsArea.Left, y + 16, NewsArea.Right, y + 16 + height),
                DT_LEFT | DT_WORDBREAK | DT_END_ELLIPSIS);
            y += 16 + height + 12;
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
        var (screenW, screenH) = GameScreen();
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
        _hot.Add((this.S(rect), () =>
        {
            click();
            InvalidateRect(_hwnd, 0, 0);
        }));
        this.Text(dc, _newsFont, index == _hover ? Hot : color ?? Ink, label, rect, DT_LEFT | DT_SINGLELINE | DT_VCENTER);
    }

    void DrawStatus(nint dc)
    {
        this.Shadowed(dc, _smallFont, Gold, _updater.Status, StatusArea, DT_CENTER | DT_WORDBREAK);
        if (_updater.Phase != Phase.Updating || _updater.Total <= 0)
            return;

        var done = Math.Clamp((double)_updater.Done / _updater.Total, 0, 1);
        var bar = this.S(Bar);
        var frame = new RECT(bar.Left - 1, bar.Top - 1, bar.Right + 1, bar.Bottom + 1);
        var fill = new RECT(bar.Left, bar.Top, bar.Left + (int)((bar.Right - bar.Left) * done), bar.Bottom);
        var border = CreateSolidBrush(Rgb(110, 88, 44));
        var back = CreateSolidBrush(Rgb(20, 14, 8));
        var gold = CreateSolidBrush(Rgb(214, 168, 64));
        FillRect(dc, &frame, border);
        FillRect(dc, &bar, back);
        FillRect(dc, &fill, gold);
        DeleteObject(border);
        DeleteObject(back);
        DeleteObject(gold);

        var amount = $"%{(int)(done * 100)}  ·  {_updater.Done >> 20} / {_updater.Total >> 20} MB";
        this.Shadowed(dc, _smallFont, Gold, amount, Amount, DT_CENTER | DT_SINGLELINE);
    }

    // w×h pixels of the image's top left at (x, y)
    void Image(nint dc, nint art, nint image, int x, int y, int w, int h)
    {
        if (image == 0)
            return;
        var old = SelectObject(art, image);
        AlphaBlend(dc, x, y, w, h, art, 0, 0, w, h, PremultipliedOver);
        SelectObject(art, old);
    }

    // The game's own minimize and close glyphs, gold with a shadow like the parchment's title
    void TitleButton(nint dc, nint art, RECT rect, nint[] images, Action click)
    {
        var index = _hot.Count;
        var pixels = this.S(rect);
        _hot.Add((pixels, click));
        var (w, h) = (this.S(rect.Right - rect.Left), this.S(rect.Bottom - rect.Top));
        var offset = Math.Max(1, this.S(1));
        this.Image(dc, art, images[2], pixels.Left + offset, pixels.Top + offset, w, h);
        this.Image(dc, art, images[index == _hover ? 1 : 0], pixels.Left, pixels.Top, w, h);
    }

    // Returns the button's index in the hot list (-1 when disabled)
    int ImageButton(nint dc, nint art, RECT rect, nint[] images, bool enabled, Action click)
    {
        var index = -1;
        var pixels = this.S(rect);
        if (enabled)
        {
            index = _hot.Count;
            _hot.Add((pixels, click));
        }
        var image = images[index >= 0 && index == _pressed && index == _hover ? 2 : index >= 0 && index == _hover ? 1 : 0];
        if (image == 0)
            return index;
        var old = SelectObject(art, image);
        var (w, h) = (this.S(rect.Right - rect.Left), this.S(rect.Bottom - rect.Top));
        AlphaBlend(dc, pixels.Left, pixels.Top, w, h, art, 0, 0, w, h, PremultipliedOver);
        SelectObject(art, old);
        return index;
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

    // Over the character: a dark copy one pixel down and right keeps it readable
    void Shadowed(nint dc, nint font, uint color, string text, RECT rect, uint format)
    {
        var pixels = this.S(rect);
        SelectObject(dc, font);
        var offset = Math.Max(1, this.S(1));
        var shadow = new RECT(pixels.Left + offset, pixels.Top + offset, pixels.Right + offset, pixels.Bottom + offset);
        SetTextColor(dc, Rgb(0, 0, 0));
        DrawText(dc, text, -1, &shadow, format | DT_NOPREFIX);
        SetTextColor(dc, color);
        DrawText(dc, text, -1, &pixels, format | DT_NOPREFIX);
    }

    // rect in art units
    void Text(nint dc, nint font, uint color, string text, RECT rect, uint format)
    {
        var pixels = this.S(rect);
        SelectObject(dc, font);
        SetTextColor(dc, color);
        DrawText(dc, text, -1, &pixels, format | DT_NOPREFIX);
    }
}
