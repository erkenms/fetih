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

    // docs/web-style.md
    static readonly uint Ink = Rgb(0x3b, 0x26, 0x14), Heading = Rgb(0x5a, 0x2a, 0x10), LinkInk = Rgb(0x7a, 0x1a, 0x12),
        HotInk = Rgb(0xa0, 0x1a, 0x10), Muted = Rgb(0x7a, 0x5a, 0x36), Divider = Rgb(0xa8, 0x8a, 0x5a);
    static readonly uint Gold = Rgb(205, 182, 92), GoldHot = Rgb(239, 207, 99);

    static LauncherWindow? _current;

    readonly Updater _updater;
    readonly double _scale;
    readonly int _width, _height;
    readonly List<(RECT Rect, Action Click)> _hot = [];
    nint _hwnd, _region;
    nint _main, _character;
    readonly nint[] _play = new nint[3], _radio = new nint[2];
    readonly Picture?[] _frame = new Picture?[3];
    readonly Dictionary<int, nint[]> _frames = [];
    // Normal, hot, shadow
    readonly nint[] _minimize = new nint[3], _close = new nint[3];
    nint _titleFont, _labelFont, _bodyFont, _boldFont, _smallFont, _statusFont, _buttonFont;
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
            _frame[i] = Art.Button($"Exit0{i + 1}.bmp");
        }
        _radio[0] = Art.Alpha("RadioOff.bmp")?.ScaleTo(this.S(16), this.S(16)).ToBitmap() ?? 0;
        _radio[1] = Art.Alpha("RadioOn.bmp")?.ScaleTo(this.S(16), this.S(16)).ToBitmap() ?? 0;
        uint[] glyphColors = [0xCDB65C, 0xEFCF63, 0x000000];
        for (var i = 0; i < 3; i++)
        {
            _minimize[i] = Art.Glyph("TMin.bmp", glyphColors[i])?.ScaleTo(this.S(20), this.S(26)).ToBitmap() ?? 0;
            _close[i] = Art.Glyph("TClose.bmp", glyphColors[i])?.ScaleTo(this.S(20), this.S(26)).ToBitmap() ?? 0;
        }
        // As the site (docs/web-style.md): Georgia bold titles, Verdana text
        _titleFont = this.Font(16, FW_BOLD, false, "Georgia");
        _labelFont = this.Font(13, FW_BOLD, false, "Georgia");
        _bodyFont = this.Font(12, FW_NORMAL, false, "Verdana");
        _boldFont = this.Font(12, FW_BOLD, false, "Verdana");
        _smallFont = this.Font(10, FW_NORMAL, false, "Verdana");
        _statusFont = this.Font(13, FW_NORMAL, false, "Tahoma");
        _buttonFont = this.Font(13, FW_BOLD, false, "Tahoma");
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
            this.DrawSettings(dc, art);
        else
            this.DrawNews(dc);

        this.DrawStatus(dc);

        var ready = _updater.Phase is Phase.Ready or Phase.Failed;
        this.ImageButton(dc, art, PlayButton, _play, ready, _updater.Phase == Phase.Failed ? _updater.Retry : this.PlayClicked);
        this.TitleButton(dc, art, MinimizeButton, _minimize, () => ShowWindow(_hwnd, SW_MINIMIZE));
        this.TitleButton(dc, art, CloseButton, _close, () => DestroyWindow(_hwnd));
        if (_updater.Phase == Phase.Ready)
            this.FrameButton(dc, art, SettingsButton, "Ayarlar", this.OpenSettings);

        // The hot list changed under the mouse: hover again on the next move
        if (_hot.Count != previousHot)
            _hover = -1;
    }

    // Like /haberler: bold subject, date on the right, dotted lines between
    void DrawNews(nint dc)
    {
        var area = NewsArea;
        this.Title(dc, "Sunucu Güncellemeleri", area.Left, area.Top);
        var y = area.Top + 26;
        var news = _updater.News;
        if (news == null)
        {
            this.Text(dc, _bodyFont, Muted, _updater.NewsFailed ? "Haberler alınamadı." : "Haberler yükleniyor...",
                new RECT(area.Left, y, area.Right, y + 18), DT_LEFT | DT_SINGLELINE);
            return;
        }

        var bottom = area.Bottom - 22;
        foreach (var (date, subject) in news)
        {
            var text = new RECT(area.Left, y, area.Right - 80, y + 32);
            var height = Math.Min(this.Measure(dc, _boldFont, subject, text), 32);
            if (y + height + 6 > bottom)
                break;
            this.Text(dc, _boldFont, Ink, subject, text with { Bottom = y + height }, DT_LEFT | DT_WORDBREAK | DT_END_ELLIPSIS);
            this.Text(dc, _smallFont, Muted, date, new RECT(area.Right - 80, y + 2, area.Right, y + 16), DT_RIGHT | DT_SINGLELINE);
            y += height + 5;
            this.Dotted(dc, area.Left, area.Right, y);
            y += 6;
        }
        this.Link(dc, new RECT(area.Right - 120, area.Bottom - 18, area.Right, area.Bottom), "Tüm haberler »",
            _updater.OpenNews, DT_RIGHT);
    }

    void DrawSettings(nint dc, nint art)
    {
        var area = NewsArea;
        var left = area.Left;
        this.Title(dc, "Ayarlar", left, area.Top);
        var y = area.Top + 30;

        y = this.Section(dc, "Ekran", y);
        this.Choice(dc, art, left + 8, y, 150, "Tam ekran", _editing.FullScreen, () => _editing = _editing with { FullScreen = true });
        this.Choice(dc, art, left + 238, y, 150, "Pencere", !_editing.FullScreen, () => _editing = _editing with { FullScreen = false });
        y += 32;

        y = this.Section(dc, "Boyut", y);
        var (screenW, screenH) = GameScreen();
        var sizes = GameSetUp.Sizes(screenW, screenH);
        if (!sizes.Contains((_editing.Width, _editing.Height)))
            sizes = [.. sizes, (_editing.Width, _editing.Height)];
        for (var i = 0; i < sizes.Count; i++)
        {
            var (w, h) = sizes[i];
            var label = i == 0 ? $"{w} × {h} (ekranın)" : $"{w} × {h}";
            this.Choice(dc, art, left + 8 + i % 2 * 230, y + i / 2 * 22, 220, label, w == _editing.Width && h == _editing.Height,
                () => _editing = _editing with { Width = w, Height = h });
        }
        y += (sizes.Count + 1) / 2 * 22 + 10;

        y = this.Section(dc, "Dosyalar", y);
        this.Text(dc, _bodyFont, Muted, "Eksik ya da bozuk dosya varsa\nhepsini denetleyip yeniden indirir.",
            new RECT(left + 8, y, area.Right - 150, y + 32), DT_LEFT | DT_WORDBREAK);
        this.FrameButton(dc, art, new RECT(area.Right - 136, y, area.Right, y + 30), "Dosyaları onar", () =>
        {
            _settingsOpen = false;
            _updater.Repair();
        });

        var bottom = area.Bottom - 30;
        this.FrameButton(dc, art, new RECT(area.Right - 196, bottom, area.Right - 104, bottom + 30), "Vazgeç", () => _settingsOpen = false);
        this.FrameButton(dc, art, new RECT(area.Right - 92, bottom, area.Right, bottom + 30), "Kaydet", this.SaveSettings);
    }

    // A Georgia title with a light shadow, as on the site
    void Title(nint dc, string text, int x, int y)
    {
        this.Text(dc, _titleFont, Rgb(250, 235, 200), text, new RECT(x + 1, y + 1, NewsArea.Right, y + 23), DT_LEFT | DT_SINGLELINE);
        this.Text(dc, _titleFont, Heading, text, new RECT(x, y, NewsArea.Right, y + 22), DT_LEFT | DT_SINGLELINE);
    }

    // A section label with a dotted line under it; returns where its content starts
    int Section(nint dc, string label, int y)
    {
        this.Text(dc, _labelFont, Heading, label, new RECT(NewsArea.Left, y, NewsArea.Right, y + 18), DT_LEFT | DT_SINGLELINE);
        this.Dotted(dc, NewsArea.Left, NewsArea.Right, y + 19);
        return y + 25;
    }

    void Dotted(nint dc, int x0, int x1, int y)
    {
        var brush = CreateSolidBrush(Divider);
        var size = Math.Max(1, this.S(1));
        for (var x = this.S(x0); x < this.S(x1); x += size * 2)
        {
            var dot = new RECT(x, this.S(y), x + size, this.S(y) + size);
            FillRect(dc, &dot, brush);
        }
        DeleteObject(brush);
    }

    // The game's radio button and a label
    void Choice(nint dc, nint art, int x, int y, int width, string label, bool selected, Action click)
    {
        var rect = new RECT(x, y, x + width, y + 18);
        var index = this.Hot(rect, click);
        var radio = this.S(new RECT(x, y + 1, x + 16, y + 17));
        this.Image(dc, art, _radio[selected ? 1 : 0], radio.Left, radio.Top, this.S(16), this.S(16));
        this.Text(dc, selected ? _boldFont : _bodyFont, index == _hover ? LinkInk : Ink, label,
            new RECT(x + 22, y, x + width, y + 18), DT_LEFT | DT_SINGLELINE | DT_VCENTER);
    }

    void Link(nint dc, RECT rect, string label, Action click, uint align = DT_LEFT)
    {
        var index = this.Hot(rect, click);
        this.Text(dc, _boldFont, index == _hover ? HotInk : LinkInk, label, rect, align | DT_SINGLELINE | DT_VCENTER);
    }

    int Hot(RECT rect, Action click)
    {
        _hot.Add((this.S(rect), () =>
        {
            click();
            InvalidateRect(_hwnd, 0, 0);
        }));
        return _hot.Count - 1;
    }

    int Measure(nint dc, nint font, string text, RECT rect)
    {
        var pixels = this.S(rect);
        SelectObject(dc, font);
        DrawText(dc, text, -1, &pixels, DT_LEFT | DT_WORDBREAK | DT_NOPREFIX | DT_CALCRECT);
        return (int)Math.Ceiling((pixels.Bottom - pixels.Top) / _scale);
    }

    // The Çık frame stretched to any width: its ends as they are, the plain texture left of
    // its text tiled between them
    nint[] Frame(int width)
    {
        if (_frames.TryGetValue(width, out var cached))
            return cached;
        var images = new nint[3];
        for (var i = 0; i < 3; i++)
        {
            if (_frame[i] is not { } exit)
                continue;
            var pixels = new uint[width * exit.Height];
            var frame = new Picture(width, exit.Height, pixels);
            frame.Blit(exit, 0, 0, 8, 0);
            for (var x = 8; x < width - 8; x += 28)
                frame.Blit(exit, 8, 0, Math.Min(28, width - 8 - x), x);
            frame.Blit(exit, exit.Width - 8, 0, 8, width - 8);
            images[i] = frame.ScaleTo(this.S(width), this.S(exit.Height)).ToBitmap();
        }
        return _frames[width] = images;
    }

    void FrameButton(nint dc, nint art, RECT rect, string label, Action click)
    {
        var index = this.ImageButton(dc, art, rect, this.Frame(rect.Right - rect.Left), true, click);
        this.Text(dc, _buttonFont, index == _hover ? GoldHot : Gold, label,
            new RECT(rect.Left, rect.Top + 4, rect.Right, rect.Bottom - 4), DT_CENTER | DT_VCENTER | DT_SINGLELINE);
    }

    void DrawStatus(nint dc)
    {
        this.Shadowed(dc, _statusFont, Gold, _updater.Status, StatusArea, DT_CENTER | DT_WORDBREAK);
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
        this.Shadowed(dc, _statusFont, Gold, amount, Amount, DT_CENTER | DT_SINGLELINE);
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
