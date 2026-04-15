using ReelsConverterUI.Animations;
using ReelsConverterUI.Models;
using ReelsConverterUI.Services;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ReelsConverterUI;

public partial class DesignerWindow : Window
{
    private readonly Rect _originRect;
    private bool _isAnimatingClose;
    private bool? _pendingResult;

    // Color data: key → (hex, label, category)
    private static readonly (string Key, string Label, string Category)[] _colorDefs =
    [
        ("BgDeep",      "Window Background", "Backgrounds"),
        ("BgSurface",   "Surface",           "Backgrounds"),
        ("BgCard",      "Card",              "Backgrounds"),
        ("BgElevated",  "Elevated / Input",  "Backgrounds"),
        ("BorderSub",   "Border",            "Borders"),
        ("Accent",      "Primary Accent",    "Accent Colors"),
        ("AccentAlt",   "Secondary Accent",  "Accent Colors"),
        ("ButtonGrad",  "Button Fill",       "Accent Colors"),
        ("TextPrimary", "Primary Text",      "Text"),
        ("TextSec",     "Secondary Text",    "Text"),
        ("SuccessGreen","Success",           "Status"),
        ("ErrorRed",    "Error",             "Status"),
    ];

    private readonly Dictionary<string, string> _colorValues = new();
    private readonly Dictionary<string, (Border swatch, TextBlock label, Border row)> _rowElements = new();
    private readonly Dictionary<string, (TextBlock header, StackPanel body, Border chevron)> _categories = new();
    private string? _activeKey;
    private bool _suppressPickerUpdate;
    private bool _suppressInputSync;
    private bool _svDragging;
    private bool _hueDragging;
    private double _currentHue;

    // Preset definitions
    private static readonly (string Tag, string Name, ThemeSettings Theme)[] _presets =
    [
        ("Default",  "Default",  ThemeService.DefaultDark),
        ("Midnight", "Midnight", ThemeService.MidnightBlue),
        ("Ocean",    "Ocean",    ThemeService.Ocean),
        ("Forest",   "Forest",   ThemeService.Forest),
        ("Warm",     "Warm",     ThemeService.Warm),
        ("Rose",     "Rosé",     ThemeService.Rose),
    ];

    public DesignerWindow(Rect originRect)
    {
        InitializeComponent();
        _originRect = originRect;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FluidMotion.MorphOpen(RootBorder, WindowScale, WindowTranslate, _originRect, this);
        BuildCategories();
        BuildPresets();
        LoadTheme(ThemeService.Current);

        // Select first color
        SelectColor(_colorDefs[0].Key);
    }

    // ════════════════════════════════════════════════════════════
    //  BUILD CATEGORY TREE
    // ════════════════════════════════════════════════════════════
    private void BuildCategories()
    {
        var grouped = _colorDefs.GroupBy(c => c.Category);

        foreach (var group in grouped)
        {
            // Category header (clickable to collapse)
            var headerGrid = new Grid
            {
                Margin = new Thickness(0),
                Cursor = Cursors.Hand
            };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });

            var headerBorder = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(12, 7, 8, 7),
            };

            var headerText = new TextBlock
            {
                Text = group.Key.ToUpperInvariant(),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("TextSec"),
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.8
            };

            var chevron = new Border
            {
                Child = new TextBlock
                {
                    Text = "▾",
                    FontSize = 9,
                    Foreground = (Brush)FindResource("TextSec"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(0)
            };

            headerBorder.Child = headerText;
            Grid.SetColumn(headerBorder, 0);
            Grid.SetColumn(chevron, 1);
            headerGrid.Children.Add(headerBorder);
            headerGrid.Children.Add(chevron);

            // Body panel with color rows
            var body = new StackPanel();

            foreach (var def in group)
            {
                _colorValues[def.Key] = "#000000";
                var row = CreateColorRow(def.Key, def.Label);
                body.Children.Add(row);
            }

            _categories[group.Key] = (headerText, body, chevron);

            // Click to toggle collapse
            headerGrid.MouseLeftButtonDown += (s, ev) => ToggleCategory(group.Key);

            CategoryPanel.Children.Add(headerGrid);
            CategoryPanel.Children.Add(body);
        }
    }

    private Border CreateColorRow(string key, string label)
    {
        var row = new Border
        {
            Padding = new Thickness(12, 5, 12, 5),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Tag = key
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var swatch = new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(swatch, 0);

        var lbl = new TextBlock
        {
            Text = label,
            FontSize = 11.5,
            Foreground = (Brush)FindResource("TextSec"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        Grid.SetColumn(lbl, 1);

        grid.Children.Add(swatch);
        grid.Children.Add(lbl);
        row.Child = grid;

        _rowElements[key] = (swatch, lbl, row);

        row.MouseLeftButtonDown += (s, e) =>
        {
            SelectColor(key);
            e.Handled = true;
        };

        row.MouseEnter += (s, e) =>
        {
            if (key != _activeKey)
                row.Background = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
        };
        row.MouseLeave += (s, e) =>
        {
            if (key != _activeKey)
                row.Background = Brushes.Transparent;
        };

        return row;
    }

    private void ToggleCategory(string categoryName)
    {
        if (!_categories.TryGetValue(categoryName, out var cat)) return;
        var body = cat.body;
        var chevron = cat.chevron;

        if (body.Visibility == Visibility.Visible)
        {
            // Collapse
            body.Visibility = Visibility.Collapsed;
            var rot = (RotateTransform)chevron.RenderTransform;
            rot.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(-90, TimeSpan.FromMilliseconds(200))
                { EasingFunction = AppleSpringEase.Snappy });
        }
        else
        {
            // Expand
            body.Visibility = Visibility.Visible;
            var rot = (RotateTransform)chevron.RenderTransform;
            rot.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(300))
                { EasingFunction = AppleSpringEase.Interactive });
        }
    }

    // ════════════════════════════════════════════════════════════
    //  BUILD PRESETS
    // ════════════════════════════════════════════════════════════
    private void BuildPresets()
    {
        foreach (var (tag, name, theme) in _presets)
        {
            var btn = new Button
            {
                Tag = tag,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(6, 4, 10, 4),
                Cursor = Cursors.Hand,
                Style = (Style)FindResource("OutlineBtn"),
                FontSize = 11
            };

            // Button content: accent dot + name
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = ThemeService.TryParseColor(theme.Accent, out var ac)
                    ? new SolidColorBrush(ac) : Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });
            sp.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            btn.Content = sp;

            btn.Click += Preset_Click;
            PresetsPanel.Children.Add(btn);
        }
    }

    // ════════════════════════════════════════════════════════════
    //  COLOR SELECTION
    // ════════════════════════════════════════════════════════════
    private void SelectColor(string key)
    {
        // Deselect old
        if (_activeKey != null && _rowElements.TryGetValue(_activeKey, out var oldEl))
        {
            oldEl.row.Background = Brushes.Transparent;
            oldEl.label.Foreground = (Brush)FindResource("TextSec");
        }

        _activeKey = key;

        // Highlight new
        if (_rowElements.TryGetValue(key, out var el))
        {
            var accentBrush = (Brush)FindResource("Accent");
            el.row.Background = new SolidColorBrush(Color.FromArgb(0x20, 0x7A, 0x9E, 0xC0));
            el.label.Foreground = (Brush)FindResource("TextPrimary");

            // Update header
            TxtActiveLabel.Text = _colorDefs.First(c => c.Key == key).Label;
            TxtActiveKey.Text = key;
        }

        // Sync picker with current value
        if (_colorValues.TryGetValue(key, out var hex) && ThemeService.TryParseColor(hex, out var color))
        {
            _suppressPickerUpdate = true;
            TxtHexInput.Text = hex;
            ActiveSwatch.Background = new SolidColorBrush(color);
            SyncRgbFields(color);
            SyncPickerFromColor(color);
            _suppressPickerUpdate = false;
        }
    }

    // ════════════════════════════════════════════════════════════
    //  COLOR PICKER: SV CANVAS
    // ════════════════════════════════════════════════════════════
    private void RenderSvCanvas()
    {
        double w = SvCanvas.ActualWidth;
        double h = SvCanvas.ActualHeight;
        if (w < 1 || h < 1) return;

        int pw = (int)w;
        int ph = (int)h;
        var wb = new WriteableBitmap(pw, ph, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[pw * ph * 4];

        for (int y = 0; y < ph; y++)
        {
            double val = 1.0 - (double)y / (ph - 1);
            for (int x = 0; x < pw; x++)
            {
                double sat = (double)x / (pw - 1);
                HsvToRgb(_currentHue, sat, val, out var r, out var g, out var b);
                int i = (y * pw + x) * 4;
                pixels[i + 0] = b;
                pixels[i + 1] = g;
                pixels[i + 2] = r;
                pixels[i + 3] = 255;
            }
        }

        wb.WritePixels(new Int32Rect(0, 0, pw, ph), pixels, pw * 4, 0);
        SvCanvas.Background = new ImageBrush(wb) { Stretch = Stretch.Fill };
    }

    private void SvCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _svDragging = true;
        SvCanvas.CaptureMouse();
        UpdateSvFromMouse(e.GetPosition(SvCanvas));
    }

    private void SvCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_svDragging) UpdateSvFromMouse(e.GetPosition(SvCanvas));
    }

    private void SvCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _svDragging = false;
        SvCanvas.ReleaseMouseCapture();
    }

    private void UpdateSvFromMouse(Point pos)
    {
        double w = SvCanvas.ActualWidth;
        double h = SvCanvas.ActualHeight;
        if (w < 1 || h < 1) return;

        double sat = Math.Clamp(pos.X / w, 0, 1);
        double val = Math.Clamp(1.0 - pos.Y / h, 0, 1);

        PositionSvThumb(sat, val);
        HsvToRgb(_currentHue, sat, val, out var r, out var g, out var b);
        var color = Color.FromRgb(r, g, b);
        ApplyColorFromPicker(color);
    }

    private void PositionSvThumb(double sat, double val)
    {
        double w = SvCanvas.ActualWidth;
        double h = SvCanvas.ActualHeight;
        Canvas.SetLeft(SvThumb, sat * w - SvThumb.Width / 2);
        Canvas.SetTop(SvThumb, (1 - val) * h - SvThumb.Height / 2);
    }

    // ════════════════════════════════════════════════════════════
    //  COLOR PICKER: HUE STRIP
    // ════════════════════════════════════════════════════════════
    private void HueCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _hueDragging = true;
        HueCanvas.CaptureMouse();
        UpdateHueFromMouse(e.GetPosition(HueCanvas));
    }

    private void HueCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_hueDragging) UpdateHueFromMouse(e.GetPosition(HueCanvas));
    }

    private void HueCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _hueDragging = false;
        HueCanvas.ReleaseMouseCapture();
    }

    private void UpdateHueFromMouse(Point pos)
    {
        double h = HueCanvas.ActualHeight;
        if (h < 1) return;

        _currentHue = Math.Clamp(pos.Y / h * 360.0, 0, 360);
        PositionHueThumb();
        RenderSvCanvas();

        // Recalc color from current SV position
        double w = SvCanvas.ActualWidth;
        double sh = SvCanvas.ActualHeight;
        if (w > 0 && sh > 0)
        {
            double sat = Math.Clamp((Canvas.GetLeft(SvThumb) + SvThumb.Width / 2) / w, 0, 1);
            double val = Math.Clamp(1.0 - (Canvas.GetTop(SvThumb) + SvThumb.Height / 2) / sh, 0, 1);
            HsvToRgb(_currentHue, sat, val, out var r, out var g, out var b);
            ApplyColorFromPicker(Color.FromRgb(r, g, b));
        }
    }

    private void PositionHueThumb()
    {
        double h = HueCanvas.ActualHeight;
        double y = _currentHue / 360.0 * h;
        Canvas.SetTop(HueThumb, y - HueThumb.Height / 2);
        HueThumb.Width = HueCanvas.ActualWidth;
        Canvas.SetLeft(HueThumb, 0);
    }

    // ════════════════════════════════════════════════════════════
    //  APPLY COLOR FROM PICKER
    // ════════════════════════════════════════════════════════════
    private void ApplyColorFromPicker(Color color)
    {
        if (_activeKey == null) return;
        var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        _colorValues[_activeKey] = hex;

        // Update swatch in tree
        if (_rowElements.TryGetValue(_activeKey, out var el))
            el.swatch.Background = new SolidColorBrush(color);

        // Update header swatch
        ActiveSwatch.Background = new SolidColorBrush(color);

        // Update text fields without re-triggering picker
        _suppressPickerUpdate = true;
        TxtHexInput.Text = hex;
        SyncRgbFields(color);
        _suppressPickerUpdate = false;

        UpdatePreview();
    }

    // ════════════════════════════════════════════════════════════
    //  TEXT INPUTS → PICKER SYNC
    // ════════════════════════════════════════════════════════════
    private void TxtHexInput_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressPickerUpdate || _suppressInputSync) return;
        var hex = TxtHexInput.Text.Trim();
        if (!ThemeService.TryParseColor(hex, out var color)) return;
        if (_activeKey == null) return;

        _colorValues[_activeKey] = hex;

        if (_rowElements.TryGetValue(_activeKey, out var el))
            el.swatch.Background = new SolidColorBrush(color);
        ActiveSwatch.Background = new SolidColorBrush(color);

        _suppressInputSync = true;
        SyncRgbFields(color);
        _suppressInputSync = false;

        SyncPickerFromColor(color);
        UpdatePreview();
    }

    private void RgbInput_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressPickerUpdate || _suppressInputSync) return;
        if (!byte.TryParse(TxtR.Text, out var r)) return;
        if (!byte.TryParse(TxtG.Text, out var g)) return;
        if (!byte.TryParse(TxtB.Text, out var b)) return;

        var color = Color.FromRgb(r, g, b);
        var hex = $"#{r:X2}{g:X2}{b:X2}";
        if (_activeKey == null) return;

        _colorValues[_activeKey] = hex;

        if (_rowElements.TryGetValue(_activeKey, out var el))
            el.swatch.Background = new SolidColorBrush(color);
        ActiveSwatch.Background = new SolidColorBrush(color);

        _suppressInputSync = true;
        TxtHexInput.Text = hex;
        _suppressInputSync = false;

        SyncPickerFromColor(color);
        UpdatePreview();
    }

    private void SyncRgbFields(Color c)
    {
        _suppressInputSync = true;
        TxtR.Text = c.R.ToString();
        TxtG.Text = c.G.ToString();
        TxtB.Text = c.B.ToString();
        _suppressInputSync = false;
    }

    private void SyncPickerFromColor(Color color)
    {
        RgbToHsv(color.R, color.G, color.B, out var h, out var s, out var v);
        _currentHue = h;

        if (SvCanvas.ActualWidth > 0)
        {
            RenderSvCanvas();
            PositionSvThumb(s, v);
            PositionHueThumb();
        }
        else
        {
            // Defer until layout is ready
            SvCanvas.Loaded += (_, _) =>
            {
                RenderSvCanvas();
                PositionSvThumb(s, v);
                PositionHueThumb();
            };
        }
    }

    // ════════════════════════════════════════════════════════════
    //  LIVE PREVIEW
    // ════════════════════════════════════════════════════════════
    private void UpdatePreview()
    {
        if (PrvOuter == null) return;

        SetPreviewBg(PrvOuter, "BgDeep");
        SetPreviewBg(PrvCard, "BgCard");
        SetPreviewBorder(PrvCard, "BorderSub");
        SetPreviewFg(PrvTitle, "TextPrimary");
        SetPreviewFg(PrvSubtitle, "TextSec");
        SetPreviewBg(PrvButton, "ButtonGrad");
        SetPreviewFg(PrvBtnText, "TextPrimary");
        SetPreviewBg(PrvInput, "BgElevated");
        SetPreviewBorder(PrvInput, "BorderSub");
        SetPreviewFg(PrvInputText, "TextSec");
        SetPreviewBg(PrvDotOk, "SuccessGreen");
        SetPreviewBg(PrvDotErr, "ErrorRed");
    }

    private void SetPreviewBg(Border b, string key)
    {
        if (_colorValues.TryGetValue(key, out var hex) && ThemeService.TryParseColor(hex, out var c))
            b.Background = new SolidColorBrush(c);
    }

    private void SetPreviewBorder(Border b, string key)
    {
        if (_colorValues.TryGetValue(key, out var hex) && ThemeService.TryParseColor(hex, out var c))
            b.BorderBrush = new SolidColorBrush(c);
    }

    private void SetPreviewFg(TextBlock t, string key)
    {
        if (_colorValues.TryGetValue(key, out var hex) && ThemeService.TryParseColor(hex, out var c))
            t.Foreground = new SolidColorBrush(c);
    }

    // ════════════════════════════════════════════════════════════
    //  THEME LOAD / READ
    // ════════════════════════════════════════════════════════════
    private void LoadTheme(ThemeSettings theme)
    {
        _suppressPickerUpdate = true;

        SetValue("BgDeep", theme.BgDeep);
        SetValue("BgSurface", theme.BgSurface);
        SetValue("BgCard", theme.BgCard);
        SetValue("BgElevated", theme.BgElevated);
        SetValue("BorderSub", theme.BorderSub);
        SetValue("Accent", theme.Accent);
        SetValue("AccentAlt", theme.AccentAlt);
        SetValue("ButtonGrad", theme.ButtonGrad);
        SetValue("TextPrimary", theme.TextPrimary);
        SetValue("TextSec", theme.TextSec);
        SetValue("SuccessGreen", theme.SuccessGreen);
        SetValue("ErrorRed", theme.ErrorRed);

        _suppressPickerUpdate = false;

        // Refresh active selection
        if (_activeKey != null)
            SelectColor(_activeKey);

        UpdatePreview();
    }

    private void SetValue(string key, string hex)
    {
        _colorValues[key] = hex;
        if (_rowElements.TryGetValue(key, out var el) && ThemeService.TryParseColor(hex, out var c))
            el.swatch.Background = new SolidColorBrush(c);
    }

    private ThemeSettings ReadTheme() => new()
    {
        BgDeep = GetHex("BgDeep"),
        BgSurface = GetHex("BgSurface"),
        BgCard = GetHex("BgCard"),
        BgElevated = GetHex("BgElevated"),
        BorderSub = GetHex("BorderSub"),
        Accent = GetHex("Accent"),
        AccentAlt = GetHex("AccentAlt"),
        ButtonGrad = GetHex("ButtonGrad"),
        TextPrimary = GetHex("TextPrimary"),
        TextSec = GetHex("TextSec"),
        SuccessGreen = GetHex("SuccessGreen"),
        ErrorRed = GetHex("ErrorRed"),
    };

    private string GetHex(string key) =>
        _colorValues.TryGetValue(key, out var hex) ? hex : "#000000";

    // ════════════════════════════════════════════════════════════
    //  PRESETS
    // ════════════════════════════════════════════════════════════
    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        var theme = _presets.FirstOrDefault(p => p.Tag == tag).Theme ?? ThemeService.DefaultDark;
        LoadTheme(theme);
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
        => LoadTheme(ThemeService.DefaultDark);

    // ════════════════════════════════════════════════════════════
    //  SAVE / CANCEL / CLOSE
    // ════════════════════════════════════════════════════════════
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var theme = ReadTheme();
        ThemeService.Save(theme);
        ThemeService.Apply(theme);
        CloseWithAnimation(true);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => CloseWithAnimation(false);

    private void TitleBar_Drag(object s, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isAnimatingClose) { e.Cancel = true; CloseWithAnimation(false); }
        base.OnClosing(e);
    }

    private void CloseWithAnimation(bool? result)
    {
        if (_isAnimatingClose) return;
        _isAnimatingClose = true;
        _pendingResult = result;
        FluidMotion.MorphClose(RootBorder, WindowScale, WindowTranslate, _originRect, this,
            () => DialogResult = _pendingResult);
    }

    // ════════════════════════════════════════════════════════════
    //  HSV ↔ RGB
    // ════════════════════════════════════════════════════════════
    private static void HsvToRgb(double h, double s, double v, out byte r, out byte g, out byte b)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60.0 % 2 - 1));
        double m = v - c;
        double r1, g1, b1;

        if (h < 60) { r1 = c; g1 = x; b1 = 0; }
        else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
        else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
        else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
        else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
        else { r1 = c; g1 = 0; b1 = x; }

        r = (byte)Math.Clamp((r1 + m) * 255 + 0.5, 0, 255);
        g = (byte)Math.Clamp((g1 + m) * 255 + 0.5, 0, 255);
        b = (byte)Math.Clamp((b1 + m) * 255 + 0.5, 0, 255);
    }

    private static void RgbToHsv(byte r, byte g, byte b, out double h, out double s, out double v)
    {
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        double delta = max - min;

        v = max;
        s = max == 0 ? 0 : delta / max;

        if (delta == 0)
            h = 0;
        else if (max == rd)
            h = 60 * (((gd - bd) / delta) % 6);
        else if (max == gd)
            h = 60 * ((bd - rd) / delta + 2);
        else
            h = 60 * ((rd - gd) / delta + 4);

        if (h < 0) h += 360;
    }
}
