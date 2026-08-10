using ReelsConverterUI.Animations;
using ReelsConverterUI.Models;
using ReelsConverterUI.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ReelsConverterUI;

public partial class DesignerWindow : Window
{
    private readonly Rect _originRect;
    private bool _isAnimatingClose;
    private bool? _pendingResult;
    public bool IsSaved { get; private set; } = false;

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
    private bool _suppressSettingsUpdate;
    private bool _suppressInputSync;
    private double _currentHue;
    private ThemeSettings _originalTheme;
    private AppSettings _originalSettings;
    private readonly ThemeSettings _initialThemeOnOpen;
    private readonly AppSettings _initialSettingsOnOpen;
    private bool _isExplicitCancel;
    private string? _currentPresetName;
    private string? _currentGradientEffectMode;
    private string? _currentAnimationPreset;
    private string? _currentWindowAnimEasing;
    private string? _currentWindowAnimStyle;
    private string? _currentDropdownAnimStyle;
    private string? _currentTabSwitchTransition;
    private double _currentBgGradientStrength = 1.0;
    private bool _currentEnableBgGradient = true;
    private bool _currentEnableThumbnailGradient = true;
    private bool _currentThumbnailGradientOnly = false;
    private bool _currentDisableThumbnailCard = false;
    private int _currentTabIndex = 0;

    // Preset definitions sorted by color spectrum (Light/Beige -> Red/Orange/Gold/Brown -> Green/Lime -> Cyan/Blue -> Purple/Pink)
    private static readonly (string Tag, string Name, ThemeSettings Theme)[] _presets =
    [
        ("Alabaster","Alabaster", ThemeService.Alabaster),
        ("Sandstone","Sandstone", ThemeService.Sandstone),
        ("Mint",     "Mint Fresh",   ThemeService.MintFresh),
        ("Crimson",  "Crimson Red",  ThemeService.CrimsonRed),
        ("Sunset",   "Sunset Glow",  ThemeService.SunsetGlow),
        ("Gold",     "Midnight Gold", ThemeService.MidnightGold),
        ("Warm",     "Warm",     ThemeService.Warm),
        ("Lime",     "Cyber Lime",   ThemeService.CyberLime),
        ("Emerald",  "Emerald",  ThemeService.Emerald),
        ("Forest",   "Forest",   ThemeService.Forest),
        ("Cyberpunk","Neon Cyber",ThemeService.Cyberpunk),
        ("Ocean",    "Ocean",    ThemeService.Ocean),
        ("Midnight", "Midnight", ThemeService.MidnightBlue),
        ("Nordic",   "Ice Nordic",ThemeService.Nordic),
        ("Default",  "Default",  ThemeService.DefaultDark),
        ("Oled",     "OLED Dark", ThemeService.OledDark),
        ("Amethyst", "Amethyst",     ThemeService.Amethyst),
        ("Dracula",  "Dracula",  ThemeService.Dracula),
        ("Aurora",   "Aurora",   ThemeService.Aurora),
        ("Rose",     "Rosé",     ThemeService.Rose),
    ];

    public DesignerWindow(Rect originRect)
    {
        InitializeComponent();
        _originRect = originRect;
        _initialThemeOnOpen = ThemeService.Current.Clone();
        _initialSettingsOnOpen = SettingsService.Current.Clone();
        _originalTheme = ThemeService.Current.Clone();
        _originalSettings = SettingsService.Current.Clone();
        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        WindowBlurHelper.EnableBlurWithFade(this, RootBorder);
    }

    private void WindowCornerGrip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            SettingsService.StartWindowResizeBottomRight(this);
    }

    private void WindowCornerGrip_MouseEnter(object sender, MouseEventArgs e)
        => SettingsService.HandleGripHover(sender, true);

    private void WindowCornerGrip_MouseLeave(object sender, MouseEventArgs e)
        => SettingsService.HandleGripHover(sender, false);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SettingsService.SettingsChanged += (_, _) => SettingsService.ApplyResizeGripVisibility(this);
        SettingsService.ApplyResizeGripVisibility(this);
        SettingsService.ApplyWindowSize(this);
        Width = 680;
        Height = 500;
        WindowBlurHelper.EnableBlurWithFade(this, RootBorder);
        WindowBlurHelper.ApplyRoundedRegion(this);
        FluidMotion.MorphOpen(RootBorder, WindowScale, WindowTranslate, _originRect, this);
        BuildCategories();
        BuildPresets();
        BuildCustomPresets();
        BuildPalette();
        LoadTheme(ThemeService.Current);

        // Load Liquid Glass checkbox states
        _suppressSettingsUpdate = true;
        var settings = Services.SettingsService.Current;
        ChkBlurMain.IsChecked = settings.BlurMainWindow;
        ChkBlurEditor.IsChecked = settings.BlurEditor;
        ChkBlurSettings.IsChecked = settings.BlurSettings;
        ChkBlurLogViewer.IsChecked = settings.BlurLogViewer;
        ChkBlurDevConsole.IsChecked = settings.BlurDevConsole;
        ChkBlurDescEditor.IsChecked = settings.BlurDescEditor;
        // Load Layout tab checkbox states
        ChkCompactMode.IsChecked    = settings.CompactMode;
        ChkHideScrollbars.IsChecked = settings.HideScrollbars;
        ChkAutoProgress.IsChecked   = settings.AutoShowProgressWindow;
        _suppressSettingsUpdate = false;

        // Select first color
        SelectColor(_colorDefs[0].Key);
    }

    // ════════════════════════════════════════════════════════════
    //  BUILD CATEGORY TREE
    // ════════════════════════════════════════════════════════════
    private void BuildCategories()
    {
        CategoryPanel.Children.Clear();
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
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.9
            };
            headerText.SetResourceReference(TextBlock.ForegroundProperty, "TextSec");
            string? catResKey = group.Key switch
            {
                "Backgrounds" => "DesignerBackgrounds",
                "Borders" => "DesignerBorders",
                "Accent Colors" => "DesignerAccent",
                "Text" => "DesignerText",
                "Status" => "DesignerStatus",
                _ => null
            };
            if (catResKey != null)
                headerText.SetResourceReference(TextBlock.TextProperty, catResKey);
            else
                headerText.Text = group.Key.ToUpperInvariant();

            var chevronTxt = new TextBlock
            {
                Text = "▾",
                FontSize = 9,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.85
            };
            chevronTxt.SetResourceReference(TextBlock.ForegroundProperty, "TextSec");

            var chevron = new Border
            {
                Child = chevronTxt,
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
            FontSize = 11.5,
            Opacity = 0.85,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        lbl.SetResourceReference(TextBlock.ForegroundProperty, "TextSec");
        lbl.SetResourceReference(TextBlock.TextProperty, "ColorLabel" + key);
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
                row.SetResourceReference(Border.BackgroundProperty, "HoverBg");
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
        PresetsPanel.Children.Clear();
        foreach (var (tag, name, theme) in _presets)
        {
            var card = new Border
            {
                Width = 114,
                Height = 56,
                Margin = new Thickness(4, 4, 4, 8),
                CornerRadius = new CornerRadius(8),
                Background = ThemeService.TryParseColor(theme.BgSurface, out var bgSurf)
                    ? new SolidColorBrush(bgSurf) : new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x24)),
                BorderBrush = ThemeService.TryParseColor(theme.BorderSub, out var borderCol)
                    ? new SolidColorBrush(borderCol) : new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x3D)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Tag = tag
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var nameTxt = new TextBlock
            {
                Text = name,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = ThemeService.TryParseColor(theme.TextPrimary, out var txtColor)
                    ? new SolidColorBrush(txtColor) : Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 2)
            };
            Grid.SetRow(nameTxt, 0);
            grid.Children.Add(nameTxt);

            var swatchPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 4)
            };
            Grid.SetRow(swatchPanel, 2);
            grid.Children.Add(swatchPanel);

            string[] presetColors = { theme.BgDeep, theme.BgCard, theme.Accent, theme.TextPrimary };
            double leftMargin = 0;
            foreach (var colHex in presetColors)
            {
                if (ThemeService.TryParseColor(colHex, out var swatchCol))
                {
                    double swatchLuminance = (0.2126 * swatchCol.R + 0.7152 * swatchCol.G + 0.0722 * swatchCol.B) / 255.0;
                    bool swatchIsLight = swatchLuminance > 0.7;

                    var dot = new Border
                    {
                        Width = 12,
                        Height = 12,
                        CornerRadius = new CornerRadius(6),
                        Background = new SolidColorBrush(swatchCol),
                        BorderBrush = swatchIsLight 
                            ? new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)) 
                            : new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)),
                        BorderThickness = new Thickness(1),
                        Margin = new Thickness(leftMargin, 0, 0, 0)
                    };
                    swatchPanel.Children.Add(dot);
                    leftMargin = -3; // overlaps slightly
                }
            }

            card.Child = grid;

            card.MouseLeftButtonDown += (s, e) =>
            {
                LoadTheme(theme, applyPreview: true);
                e.Handled = true;
            };

            card.MouseEnter += (s, e) =>
            {
                card.BorderBrush = ThemeService.TryParseColor(theme.Accent, out var accCol)
                    ? new SolidColorBrush(accCol) : Brushes.White;
                card.RenderTransform = new ScaleTransform(1.03, 1.03);
                card.RenderTransformOrigin = new Point(0.5, 0.5);
            };
            card.MouseLeave += (s, e) =>
            {
                card.BorderBrush = ThemeService.TryParseColor(theme.BorderSub, out var borderColOld)
                    ? new SolidColorBrush(borderColOld) : new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x3D));
                card.RenderTransform = null;
            };

            PresetsPanel.Children.Add(card);
        }
    }

    private void BuildCustomPresets()
    {
        if (CustomPresetsPanel == null) return;
        CustomPresetsPanel.Children.Clear();

        var addNewCard = CreateAddNewPresetCard();
        CustomPresetsPanel.Children.Add(addNewCard);

        var customList = ThemeService.LoadCustomPresets();
        foreach (var theme in customList)
        {
            var card = CreateCustomPresetCard(theme);
            CustomPresetsPanel.Children.Add(card);
        }
    }

    private Border CreateCustomPresetCard(ThemeSettings theme)
    {
        var card = new Border
        {
            Width = 114,
            Height = 56,
            Margin = new Thickness(4, 4, 4, 8),
            CornerRadius = new CornerRadius(8),
            Background = ThemeService.TryParseColor(theme.BgSurface, out var bgSurf)
                ? new SolidColorBrush(bgSurf) : new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x24)),
            BorderBrush = ThemeService.TryParseColor(theme.BorderSub, out var borderCol)
                ? new SolidColorBrush(borderCol) : new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x3D)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var nameTxt = new TextBlock
        {
            Text = theme.PresetName,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeService.TryParseColor(theme.TextPrimary, out var txtColor)
                ? new SolidColorBrush(txtColor) : Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 4, 6, 2),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetRow(nameTxt, 0);
        grid.Children.Add(nameTxt);

        var swatchPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 4)
        };
        Grid.SetRow(swatchPanel, 2);
        grid.Children.Add(swatchPanel);

        string[] presetColors = { theme.BgDeep, theme.BgCard, theme.Accent, theme.TextPrimary };
        double leftMargin = 0;
        foreach (var colHex in presetColors)
        {
            if (ThemeService.TryParseColor(colHex, out var swatchCol))
            {
                double swatchLuminance = (0.2126 * swatchCol.R + 0.7152 * swatchCol.G + 0.0722 * swatchCol.B) / 255.0;
                bool swatchIsLight = swatchLuminance > 0.7;

                var dot = new Border
                {
                    Width = 12,
                    Height = 12,
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(swatchCol),
                    BorderBrush = swatchIsLight 
                        ? new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)) 
                        : new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(leftMargin, 0, 0, 0)
                };
                swatchPanel.Children.Add(dot);
                leftMargin = -3;
            }
        }

        var deleteBtn = new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Color.FromArgb(0x80, 0x22, 0x22, 0x22)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 2, 0),
            Cursor = Cursors.Hand,
            Visibility = Visibility.Collapsed,
            Child = new TextBlock
            {
                Text = "✕",
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        grid.Children.Add(deleteBtn);

        card.Child = grid;

        card.MouseLeftButtonDown += (s, e) =>
        {
            LoadTheme(theme, applyPreview: true);
            e.Handled = true;
        };

        card.MouseEnter += (s, e) =>
        {
            card.BorderBrush = ThemeService.TryParseColor(theme.Accent, out var accCol)
                ? new SolidColorBrush(accCol) : Brushes.White;
            card.RenderTransform = new ScaleTransform(1.03, 1.03);
            card.RenderTransformOrigin = new Point(0.5, 0.5);
            deleteBtn.Visibility = Visibility.Visible;
        };

        card.MouseLeave += (s, e) =>
        {
            card.BorderBrush = ThemeService.TryParseColor(theme.BorderSub, out var borderColOld)
                ? new SolidColorBrush(borderColOld) : new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x3D));
            card.RenderTransform = null;
            deleteBtn.Visibility = Visibility.Collapsed;
        };

        deleteBtn.MouseLeftButtonDown += (s, e) =>
        {
            e.Handled = true;
            DeleteCustomPreset(theme);
        };

        return card;
    }

    private Border CreateAddNewPresetCard()
    {
        var card = new Border
        {
            Width = 114,
            Height = 56,
            Margin = new Thickness(4, 4, 4, 8),
            CornerRadius = new CornerRadius(8),
            Background = (Brush)Application.Current.Resources["InputBg"] ?? Brushes.Transparent,
            BorderBrush = (Brush)Application.Current.Resources["BorderSub"] ?? Brushes.Gray,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand
        };

        var grid = new Grid();
        var plusTxt = new TextBlock
        {
            Text = "+",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["TextSec"] ?? Brushes.Gray,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(plusTxt);
        card.Child = grid;

        card.MouseLeftButtonDown += (s, e) =>
        {
            e.Handled = true;
            ShowAddPresetDialog();
        };

        card.MouseEnter += (s, e) =>
        {
            card.BorderBrush = (Brush)Application.Current.Resources["Accent"] ?? Brushes.White;
            card.RenderTransform = new ScaleTransform(1.03, 1.03);
            card.RenderTransformOrigin = new Point(0.5, 0.5);
            plusTxt.Foreground = (Brush)Application.Current.Resources["TextPrimary"] ?? Brushes.White;
        };

        card.MouseLeave += (s, e) =>
        {
            card.BorderBrush = (Brush)Application.Current.Resources["BorderSub"] ?? Brushes.Gray;
            card.RenderTransform = null;
            plusTxt.Foreground = (Brush)Application.Current.Resources["TextSec"] ?? Brushes.Gray;
        };

        return card;
    }

    private void ShowAddPresetDialog()
    {
        TxtOverlayPresetName.Text = "My Custom Theme";
        DlgOverlay.Visibility = Visibility.Visible;
        TxtOverlayPresetName.Focus();
        TxtOverlayPresetName.SelectAll();
    }

    private void CancelOverlay_Click(object sender, RoutedEventArgs e)
    {
        DlgOverlay.Visibility = Visibility.Collapsed;
    }

    private void SaveOverlay_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtOverlayPresetName.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var currentPreset = ReadTheme();
        currentPreset.PresetName = name;

        var presets = ThemeService.LoadCustomPresets();
        presets.RemoveAll(p => p.PresetName.Equals(name, StringComparison.OrdinalIgnoreCase));
        presets.Add(currentPreset);
        ThemeService.SaveCustomPresets(presets);

        BuildCustomPresets();
        DlgOverlay.Visibility = Visibility.Collapsed;
    }

    private void DeleteCustomPreset(ThemeSettings theme)
    {
        var presets = ThemeService.LoadCustomPresets();
        presets.RemoveAll(p => p.PresetName.Equals(theme.PresetName, StringComparison.Ordinal));
        ThemeService.SaveCustomPresets(presets);
        BuildCustomPresets();
    }

    // ════════════════════════════════════════════════════════════
    //  BUILD CURATED PALETTE GRID
    // ════════════════════════════════════════════════════════════
    private void BuildPalette()
    {
        PaletteGridPanel.Children.Clear();
        string[] colors =
        [
            "#EF4444", "#F43F5E", "#EC4899", "#A855F7", "#8B5CF6", "#6366F1",
            "#3B82F6", "#0EA5E9", "#06B6D4", "#14B8A6", "#10B981", "#22C55E",
            "#84CC16", "#EAB308", "#F59E0B", "#F97316", "#64748B", "#94A3B8"
        ];

        foreach (var hex in colors)
        {
            if (ThemeService.TryParseColor(hex, out var c))
            {
                double swatchLuminance = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
                bool swatchIsLight = swatchLuminance > 0.7;
                var normalBorder = swatchIsLight 
                    ? new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)) 
                    : new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255));
                var hoverBorder = swatchIsLight 
                    ? Brushes.Black 
                    : Brushes.White;

                var border = new Border
                {
                    Width = 14,
                    Height = 14,
                    CornerRadius = new CornerRadius(7),
                    Background = new SolidColorBrush(c),
                    BorderBrush = normalBorder,
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(3),
                    Cursor = Cursors.Hand,
                    Tag = hex
                };

                border.MouseLeftButtonDown += (s, e) =>
                {
                    if (_activeKey == null) return;
                    if (ThemeService.TryParseColor(hex, out var col))
                    {
                        ApplyColorFromPicker(col);
                        SyncPickerFromColor(col);
                    }
                    e.Handled = true;
                };

                border.MouseEnter += (s, e) =>
                {
                    border.BorderBrush = hoverBorder;
                    border.Width = 16;
                    border.Height = 16;
                    border.CornerRadius = new CornerRadius(8);
                    border.Margin = new Thickness(2);
                };
                border.MouseLeave += (s, e) =>
                {
                    border.BorderBrush = normalBorder;
                    border.Width = 14;
                    border.Height = 14;
                    border.CornerRadius = new CornerRadius(7);
                    border.Margin = new Thickness(3);
                };

                PaletteGridPanel.Children.Add(border);
            }
        }
    }

    // ════════════════════════════════════════════════════════════
    //  COLOR SELECTION
    // ════════════════════════════════════════════════════════════
    private void SelectColor(string key)
    {
        if (_activeKey != null && _rowElements.TryGetValue(_activeKey, out var oldEl))
        {
            oldEl.row.Background = Brushes.Transparent;
            oldEl.label.SetResourceReference(TextBlock.ForegroundProperty, "TextSec");
            oldEl.label.Opacity = 0.85;
            oldEl.label.FontWeight = FontWeights.Normal;
        }

        _activeKey = key;

        if (_rowElements.TryGetValue(key, out var el))
        {
            el.row.SetResourceReference(Border.BackgroundProperty, "ActiveBg");
            el.label.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
            el.label.Opacity = 1.0;
            el.label.FontWeight = FontWeights.SemiBold;

            TxtActiveLabel.SetResourceReference(TextBlock.TextProperty, "ColorLabel" + key);
            TxtActiveKey.Text = key;
        }

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
    //  APPLY COLOR FROM PICKER
    // ════════════════════════════════════════════════════════════
    private void ApplyColorFromPicker(Color color)
    {
        if (_activeKey == null) return;
        string hex;
        if (_activeKey == "BgDeep")
        {
            byte alpha = (byte)Math.Round(SliderGlassOpacity.Value / 100.0 * 255.0);
            hex = $"#{alpha:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        else
        {
            hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        _colorValues[_activeKey] = hex;

        if (_rowElements.TryGetValue(_activeKey, out var el))
            el.swatch.Background = new SolidColorBrush(color);

        ActiveSwatch.Background = new SolidColorBrush(color);

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

        if (_activeKey == "BgDeep")
        {
            if (hex.StartsWith("#") && hex.Length == 9)
            {
                try
                {
                    byte alpha = Convert.ToByte(hex.Substring(1, 2), 16);
                    double opacityPct = (alpha / 255.0) * 100.0;
                    _suppressPickerUpdate = true;
                    SliderGlassOpacity.Value = Math.Clamp(opacityPct, 50, 100);
                    TxtGlassOpacityPct.Text = $"{(int)SliderGlassOpacity.Value}%";
                    _suppressPickerUpdate = false;
                }
                catch { }
            }
            else if (hex.StartsWith("#") && hex.Length == 7)
            {
                _suppressPickerUpdate = true;
                SliderGlassOpacity.Value = 100;
                TxtGlassOpacityPct.Text = "100%";
                _suppressPickerUpdate = false;
            }
        }

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
        string hex;
        if (_activeKey == "BgDeep")
        {
            byte alpha = (byte)Math.Round(SliderGlassOpacity.Value / 100.0 * 255.0);
            hex = $"#{alpha:X2}{r:X2}{g:X2}{b:X2}";
        }
        else
        {
            hex = $"#{r:X2}{g:X2}{b:X2}";
        }

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

        _suppressPickerUpdate = true;
        if (SliderHue != null) SliderHue.Value = h;
        if (SliderSat != null) SliderSat.Value = s * 100.0;
        if (SliderVal != null) SliderVal.Value = v * 100.0;
        _suppressPickerUpdate = false;

        UpdateSliderTracks(h, s, v);
    }

    // ════════════════════════════════════════════════════════════
    //  HSL SLIDERS LOGIC
    // ════════════════════════════════════════════════════════════
    private void SliderHue_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtHueVal == null) return;
        TxtHueVal.Text = $"{(int)SliderHue.Value}°";
        if (_suppressPickerUpdate) return;
        OnSliderValueChanged();
    }

    private void SliderSat_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtSatVal == null) return;
        TxtSatVal.Text = $"{(int)SliderSat.Value}%";
        if (_suppressPickerUpdate) return;
        OnSliderValueChanged();
    }

    private void SliderVal_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtValVal == null) return;
        TxtValVal.Text = $"{(int)SliderVal.Value}%";
        if (_suppressPickerUpdate) return;
        OnSliderValueChanged();
    }

    private void OnSliderValueChanged()
    {
        double h = SliderHue.Value;
        double s = SliderSat.Value / 100.0;
        double v = SliderVal.Value / 100.0;

        UpdateSliderTracks(h, s, v);

        HsvToRgb(h, s, v, out var r, out var g, out var b);
        var color = Color.FromRgb(r, g, b);
        ApplyColorFromPicker(color);
    }

    private void UpdateSliderTracks(double h, double s, double v)
    {
        if (SatBrush == null || ValBrush == null) return;

        HsvToRgb(h, 0, v, out var rStart, out var gStart, out var bStart);
        HsvToRgb(h, 1, v, out var rEnd, out var gEnd, out var bEnd);

        if (SatBrush.IsFrozen)
        {
            var newSatBrush = new LinearGradientBrush(Color.FromRgb(rStart, gStart, bStart), Color.FromRgb(rEnd, gEnd, bEnd), 0);
            SliderSat.Background = newSatBrush;
        }
        else
        {
            SatBrush.GradientStops[0].Color = Color.FromRgb(rStart, gStart, bStart);
            SatBrush.GradientStops[1].Color = Color.FromRgb(rEnd, gEnd, bEnd);
        }

        HsvToRgb(h, s, 0, out var rValStart, out var gValStart, out var bValStart);
        HsvToRgb(h, s, 1, out var rValEnd, out var gValEnd, out var bValEnd);

        if (ValBrush.IsFrozen)
        {
            var newValBrush = new LinearGradientBrush(Color.FromRgb(rValStart, gValStart, bValStart), Color.FromRgb(rValEnd, gValEnd, bValEnd), 0);
            SliderVal.Background = newValBrush;
        }
        else
        {
            ValBrush.GradientStops[0].Color = Color.FromRgb(rValStart, gValStart, bValStart);
            ValBrush.GradientStops[1].Color = Color.FromRgb(rValEnd, gValEnd, bValEnd);
        }
    }

    // ════════════════════════════════════════════════════════════
    // ════════════════════════════════════════════════════════════
    //  TAB TRANSITION HELPERS
    // ════════════════════════════════════════════════════════════
    private FrameworkElement? GetPanel(int index) => index switch
    {
        0 => PanelPresets,
        1 => PanelCustom,
        2 => PanelCustomize,
        3 => PanelStyle,
        4 => PanelLayout,
        5 => PanelAnim,
        _ => null
    };

    private TranslateTransform? GetTranslate(int index) => index switch
    {
        0 => TransPresets,
        1 => TransCustom,
        2 => TransCustomize,
        3 => TransStyle,
        4 => TransLayout,
        5 => TransAnim,
        _ => null
    };

    private void AnimateTabTransition(int oldIndex, int newIndex)
    {
        var oldPanel = GetPanel(oldIndex);
        var newPanel = GetPanel(newIndex);
        var oldTrans = GetTranslate(oldIndex);
        var newTrans = GetTranslate(newIndex);

        if (oldPanel == null || newPanel == null || oldTrans == null || newTrans == null) return;

        var theme = ThemeService.Current ?? new ThemeSettings();
        string preset = theme.AnimationPreset ?? theme.AnimationLevel ?? "Balanced";
        string transitionStyle = theme.TabSwitchTransition ?? "Slide & Fade";

        if (preset == "Disabled (Static)" || transitionStyle == "Instant")
        {
            oldPanel.IsHitTestVisible = false;
            oldPanel.Visibility = Visibility.Collapsed;
            oldPanel.Opacity = 0;
            newPanel.Visibility = Visibility.Visible;
            newPanel.IsHitTestVisible = true;
            newPanel.Opacity = 1;
            newTrans.X = 0;
            return;
        }

        int baseMs = theme.WindowAnimDuration > 0 ? (int)(theme.WindowAnimDuration * 0.8) : 220;
        var duration = TimeSpan.FromMilliseconds(baseMs);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        bool forward = newIndex > oldIndex;
        double startX = transitionStyle == "Slide & Fade" ? (forward ? 30 : -30) : 0;
        double endX = transitionStyle == "Slide & Fade" ? (forward ? -30 : 30) : 0;

        // Outgoing Panel
        oldPanel.IsHitTestVisible = false;
        var oldFade = new DoubleAnimation(0, duration) { EasingFunction = ease };
        oldFade.Completed += (s, e) =>
        {
            if (oldPanel.Opacity == 0) oldPanel.Visibility = Visibility.Collapsed;
        };
        oldPanel.BeginAnimation(OpacityProperty, oldFade);
        if (transitionStyle == "Slide & Fade")
            oldTrans.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(endX, duration) { EasingFunction = ease });

        // Incoming Panel
        newPanel.Visibility = Visibility.Visible;
        newPanel.IsHitTestVisible = true;
        newPanel.Opacity = 0;
        newTrans.X = startX;

        var newFade = new DoubleAnimation(1, duration) { EasingFunction = ease };
        newPanel.BeginAnimation(OpacityProperty, newFade);

        if (transitionStyle == "Slide & Fade")
        {
            newTrans.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, duration) { EasingFunction = ease });
        }
        else if (transitionStyle == "Scale Morph")
        {
            var scaleAnim = new DoubleAnimation(0.94, 1.0, duration) { EasingFunction = ease };
            newPanel.RenderTransformOrigin = new Point(0.5, 0.5);
            var st = new ScaleTransform(0.94, 0.94);
            newPanel.RenderTransform = st;
            st.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
        }
    }

    private void ApplyThemeCorrectly(ThemeSettings theme)
    {
        if (theme.AdaptiveThumbnailTheme && Owner is MainWindow mainWin && mainWin.LastDominantColor is Color dominantColor)
        {
            ThemeService.Apply(MainWindow.CreateAdaptiveTheme(theme, dominantColor));
        }
        else
        {
            ThemeService.Apply(theme);
        }
    }

    // ════════════════════════════════════════════════════════════
    //  TAB NAVIGATION
    // ════════════════════════════════════════════════════════════
    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (PanelPresets == null || PanelCustom == null || PanelCustomize == null || PanelStyle == null || PanelLayout == null || PanelAnim == null) return;

        int targetIndex = 0;
        if (TabCustomBtn.IsChecked == true) targetIndex = 1;
        else if (TabCustomizeBtn.IsChecked == true) targetIndex = 2;
        else if (TabStyleBtn.IsChecked == true) targetIndex = 3;
        else if (TabLayoutBtn.IsChecked == true) targetIndex = 4;
        else if (TabAnimBtn.IsChecked == true) targetIndex = 5;

        if (BtnResetAnim != null)
            BtnResetAnim.Visibility = targetIndex == 5 ? Visibility.Visible : Visibility.Collapsed;

        if (!IsLoaded)
        {
            _currentTabIndex = targetIndex;
            PanelPresets.Visibility   = targetIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            PanelCustom.Visibility    = targetIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            PanelCustomize.Visibility = targetIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            PanelStyle.Visibility     = targetIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
            PanelLayout.Visibility    = targetIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
            PanelAnim.Visibility      = targetIndex == 5 ? Visibility.Visible : Visibility.Collapsed;
            PanelPresets.Opacity   = targetIndex == 0 ? 1 : 0;
            PanelCustom.Opacity    = targetIndex == 1 ? 1 : 0;
            PanelCustomize.Opacity = targetIndex == 2 ? 1 : 0;
            PanelStyle.Opacity     = targetIndex == 3 ? 1 : 0;
            PanelLayout.Opacity    = targetIndex == 4 ? 1 : 0;
            PanelAnim.Opacity      = targetIndex == 5 ? 1 : 0;
            return;
        }

        if (targetIndex == _currentTabIndex) return;

        int oldIndex = _currentTabIndex;
        _currentTabIndex = targetIndex;

        AnimateTabTransition(oldIndex, targetIndex);

        if (targetIndex == 2 && _activeKey != null)
        {
            SelectColor(_activeKey);
        }
    }

    // ════════════════════════════════════════════════════════════
    //  LIVE PREVIEW
    // ════════════════════════════════════════════════════════════
    private void UpdatePreview()
    {
        if (_suppressSettingsUpdate) return;
        var theme = ReadTheme();
        ApplyThemeCorrectly(theme);
        Services.ThemeService.Save(theme);
        _originalTheme = theme.Clone();
        IsSaved = true;
    }

    private void ChkAdaptiveTheme_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressPickerUpdate || _suppressSettingsUpdate) return;
        UpdatePreview();
    }

    private void ChkEnableBorders_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressPickerUpdate || _suppressSettingsUpdate) return;
        UpdatePreview();
    }

    private void HeaderLiquidGlass_Click(object sender, MouseButtonEventArgs e)
        => ToggleCollapsibleSection(PanelLiquidGlassBody, RotLiquidGlassArrow, TransLiquidGlassBody);

    private void HeaderMotionEffects_Click(object sender, MouseButtonEventArgs e)
        => ToggleCollapsibleSection(PanelMotionEffectsBody, RotMotionEffectsArrow, TransMotionEffectsBody);

    private void HeaderFineTuning_Click(object sender, MouseButtonEventArgs e)
        => ToggleCollapsibleSection(PanelFineTuningBody, RotFineTuningArrow, TransFineTuningBody);

    private void ToggleCollapsibleSection(FrameworkElement? bodyPanel, RotateTransform? arrowRot, TranslateTransform? bodyTrans)
    {
        if (bodyPanel == null || arrowRot == null || bodyTrans == null) return;

        var ease = AppleSpringEase.Interactive;
        var dur = TimeSpan.FromMilliseconds(220);

        if (bodyPanel.Visibility == Visibility.Visible)
        {
            // Collapse Animation
            arrowRot.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)) { EasingFunction = AppleSpringEase.Snappy });

            var opAnim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = AppleSpringEase.Snappy };
            opAnim.Completed += (s, args) => bodyPanel.Visibility = Visibility.Collapsed;
            bodyPanel.BeginAnimation(UIElement.OpacityProperty, opAnim);

            bodyTrans.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, -6, TimeSpan.FromMilliseconds(180)) { EasingFunction = AppleSpringEase.Snappy });
        }
        else
        {
            // Expand Animation
            bodyPanel.Visibility = Visibility.Visible;

            arrowRot.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(180, dur) { EasingFunction = ease });

            bodyPanel.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, dur) { EasingFunction = ease });

            bodyTrans.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(-6, 0, dur) { EasingFunction = ease });
        }
    }

    private void BlurCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingsUpdate) return;

        var settings = Services.SettingsService.Current;
        settings.BlurMainWindow = ChkBlurMain?.IsChecked == true;
        settings.BlurEditor = ChkBlurEditor?.IsChecked == true;
        settings.BlurSettings = ChkBlurSettings?.IsChecked == true;
        settings.BlurLogViewer = ChkBlurLogViewer?.IsChecked == true;
        settings.BlurDevConsole = ChkBlurDevConsole?.IsChecked == true;
        settings.BlurDescEditor = ChkBlurDescEditor?.IsChecked == true;

        Services.SettingsService.Save(settings);
        _originalSettings = settings.Clone();
        IsSaved = true;

        if (Owner is MainWindow mainWin)
        {
            if (settings.BlurMainWindow)
                Services.WindowBlurHelper.EnableBlurWithFade(mainWin, mainWin.RootBorder);
            else
                Services.WindowBlurHelper.DisableBlur(mainWin);
        }
    }

    private void ChkLayoutOption_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingsUpdate) return;
        var s = Services.SettingsService.Current;
        s.CompactMode             = ChkCompactMode?.IsChecked == true;
        s.HideScrollbars          = ChkHideScrollbars?.IsChecked == true;
        s.AutoShowProgressWindow  = ChkAutoProgress?.IsChecked == true;
        Services.SettingsService.Save(s);
        _originalSettings = s.Clone();
        IsSaved = true;
    }

    private void SliderGlassOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtGlassOpacityPct == null) return;
        TxtGlassOpacityPct.Text = $"{(int)SliderGlassOpacity.Value}%";
        if (_suppressPickerUpdate || _suppressSettingsUpdate) return;

        if (_colorValues.TryGetValue("BgDeep", out var hex) && ThemeService.TryParseColor(hex, out var color))
        {
            byte alpha = (byte)Math.Round(SliderGlassOpacity.Value / 100.0 * 255.0);
            var newHex = $"#{alpha:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
            _colorValues["BgDeep"] = newHex;

            if (_activeKey == "BgDeep")
            {
                _suppressPickerUpdate = true;
                TxtHexInput.Text = newHex;
                _suppressPickerUpdate = false;
            }

            UpdatePreview();
        }
    }

    // ════════════════════════════════════════════════════════════
    //  THEME LOAD / READ
    // ════════════════════════════════════════════════════════════
    private void LoadTheme(ThemeSettings theme, bool applyPreview = false)
    {
        _suppressPickerUpdate = true;
        _suppressSettingsUpdate = true;

        try
        {
            _currentPresetName = theme.PresetName;
            _currentGradientEffectMode = theme.GradientEffectMode;
            _currentAnimationPreset = theme.AnimationPreset;
            _currentWindowAnimEasing = theme.WindowAnimEasing;
            _currentWindowAnimStyle = theme.WindowAnimStyle;
            _currentDropdownAnimStyle = theme.DropdownAnimStyle;
            _currentTabSwitchTransition = theme.TabSwitchTransition;
            _currentBgGradientStrength = theme.BgGradientStrength;
            _currentEnableBgGradient = theme.EnableBgGradient;
            _currentEnableThumbnailGradient = theme.EnableThumbnailGradient;
            _currentThumbnailGradientOnly = theme.ThumbnailGradientOnly;
            _currentDisableThumbnailCard = theme.DisableThumbnailCard;

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

            if (ChkAdaptiveTheme != null)
                ChkAdaptiveTheme.IsChecked = theme.AdaptiveThumbnailTheme;
            if (ChkEnableBorders != null)
                ChkEnableBorders.IsChecked = theme.EnableBorders;

            if (CmbAnimPreset != null && !string.IsNullOrEmpty(theme.AnimationPreset))
            {
                var match = CmbAnimPreset.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Content?.ToString() == theme.AnimationPreset);
                if (match != null)
                {
                    CmbAnimPreset.SelectionChanged -= CmbAnimPreset_Changed;
                    CmbAnimPreset.SelectedItem = match;
                    CmbAnimPreset.SelectionChanged += CmbAnimPreset_Changed;
                }
                UpdatePresetCardSelection(theme.AnimationPreset);
            }

            // Load Glass Opacity from theme.BgDeep
            double opacity = 100;
            if (!string.IsNullOrEmpty(theme.BgDeep) && theme.BgDeep.StartsWith("#"))
            {
                if (theme.BgDeep.Length == 9) // #AARRGGBB
                {
                    try
                    {
                        byte alpha = Convert.ToByte(theme.BgDeep.Substring(1, 2), 16);
                        opacity = (alpha / 255.0) * 100.0;
                    }
                    catch { }
                }
            }

            if (SliderGlassOpacity != null)
            {
                SliderGlassOpacity.Value = Math.Clamp(opacity, 50, 100);
                if (TxtGlassOpacityPct != null)
                    TxtGlassOpacityPct.Text = $"{(int)SliderGlassOpacity.Value}%";
            }

            if (SliderWinDur != null) SliderWinDur.Value = theme.WindowAnimDuration > 0 ? theme.WindowAnimDuration : 380;
            if (SliderHoverScale != null) SliderHoverScale.Value = theme.ButtonHoverScale > 0 ? theme.ButtonHoverScale : 1.03;
            if (SliderProgSpeed != null) SliderProgSpeed.Value = theme.ProgressBarAnimSpeed > 0 ? theme.ProgressBarAnimSpeed : 350;
            if (ChkHoverMicro != null) ChkHoverMicro.IsChecked = theme.EnableHoverMicroAnims;
            if (ChkProgPulse != null) ChkProgPulse.IsChecked = theme.EnableProgressPulse;
            if (ChkStaggered != null) ChkStaggered.IsChecked = theme.EnableStaggeredAnimations;

            SelectComboByContent(CmbWinEasing, theme.WindowAnimEasing);
            SelectComboByContent(CmbWinStyle, theme.WindowAnimStyle);
            SelectComboByContent(CmbDropdownStyle, theme.DropdownAnimStyle);
            SelectComboByContent(CmbTabTransition, theme.TabSwitchTransition);
            SelectComboByTag(CmbGradEffectMode, theme.GradientEffectMode);
        }
        finally
        {
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            _suppressPickerUpdate = false;
            _suppressSettingsUpdate = false;
        }

        if (_activeKey != null)
            SelectColor(_activeKey);

        if (applyPreview)
            UpdatePreview();
    }

    private void SelectComboByContent(ComboBox? combo, string? value)
    {
        if (combo == null || string.IsNullOrEmpty(value)) return;
        var match = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Content?.ToString() == value);
        if (match != null)
        {
            if (combo == CmbAnimPreset) combo.SelectionChanged -= CmbAnimPreset_Changed;
            else if (combo == CmbWinEasing) combo.SelectionChanged -= CmbWinEasing_Changed;
            else if (combo == CmbWinStyle) combo.SelectionChanged -= CmbWinStyle_Changed;
            else if (combo == CmbDropdownStyle) combo.SelectionChanged -= CmbDropdownStyle_Changed;
            else if (combo == CmbTabTransition) combo.SelectionChanged -= CmbTabTransition_Changed;

            combo.SelectedItem = match;

            if (combo == CmbAnimPreset) combo.SelectionChanged += CmbAnimPreset_Changed;
            else if (combo == CmbWinEasing) combo.SelectionChanged += CmbWinEasing_Changed;
            else if (combo == CmbWinStyle) combo.SelectionChanged += CmbWinStyle_Changed;
            else if (combo == CmbDropdownStyle) combo.SelectionChanged += CmbDropdownStyle_Changed;
            else if (combo == CmbTabTransition) combo.SelectionChanged += CmbTabTransition_Changed;
        }
    }

    private void SelectComboByTag(ComboBox? combo, string? tag)
    {
        if (combo == null || string.IsNullOrEmpty(tag)) return;
        var match = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Tag?.ToString() == tag);
        if (match != null)
        {
            if (combo == CmbGradEffectMode) combo.SelectionChanged -= CmbGradEffectMode_SelectionChanged;
            combo.SelectedItem = match;
            if (combo == CmbGradEffectMode) combo.SelectionChanged += CmbGradEffectMode_SelectionChanged;
        }
    }

    private void CmbGradEffectMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettingsUpdate) return;
        if (CmbGradEffectMode?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            _currentGradientEffectMode = tag;
        }
        var theme = ReadTheme();
        ApplyThemeCorrectly(theme);
        Services.ThemeService.Save(theme);
    }

    private void SetValue(string key, string hex)
    {
        _colorValues[key] = hex;
        if (_rowElements.TryGetValue(key, out var el) && ThemeService.TryParseColor(hex, out var c))
            el.swatch.Background = new SolidColorBrush(c);
    }

    private void AnimLevel_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressPickerUpdate || _suppressSettingsUpdate) return;
        var theme = ReadTheme();
        ThemeService.Apply(theme);
    }

    private ThemeSettings ReadTheme()
    {
        var cur = ThemeService.Current;
        return new ThemeSettings
        {
            PresetName = _currentPresetName ?? cur.PresetName,
            AdaptiveThumbnailTheme = ChkAdaptiveTheme != null ? ChkAdaptiveTheme.IsChecked == true : cur.AdaptiveThumbnailTheme,
            EnableBorders = ChkEnableBorders != null ? ChkEnableBorders.IsChecked == true : cur.EnableBorders,
            GradientEffectMode = (CmbGradEffectMode?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? _currentGradientEffectMode ?? cur.GradientEffectMode ?? "thumbnail_only",
            BgGradientStrength = _currentBgGradientStrength,
            EnableBgGradient = _currentEnableBgGradient,
            EnableThumbnailGradient = _currentEnableThumbnailGradient,
            ThumbnailGradientOnly = _currentThumbnailGradientOnly,
            DisableThumbnailCard = _currentDisableThumbnailCard,
            AnimationLevel = (CmbAnimPreset?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? _currentAnimationPreset ?? cur.AnimationPreset ?? "Smooth Liquid",
            AnimationPreset = (CmbAnimPreset?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? _currentAnimationPreset ?? cur.AnimationPreset ?? "Smooth Liquid",
            WindowAnimDuration = SliderWinDur != null ? (int)Math.Round(SliderWinDur.Value) : cur.WindowAnimDuration,
            WindowAnimEasing = (CmbWinEasing?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? _currentWindowAnimEasing ?? cur.WindowAnimEasing ?? "Apple Spring (Bouncy)",
            WindowAnimStyle = (CmbWinStyle?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? _currentWindowAnimStyle ?? cur.WindowAnimStyle ?? "Morph & Scale",
            ButtonHoverScale = SliderHoverScale != null ? SliderHoverScale.Value : cur.ButtonHoverScale,
            EnableHoverMicroAnims = ChkHoverMicro != null ? ChkHoverMicro.IsChecked == true : cur.EnableHoverMicroAnims,
            ProgressBarAnimSpeed = SliderProgSpeed != null ? (int)Math.Round(SliderProgSpeed.Value) : cur.ProgressBarAnimSpeed,
            EnableProgressPulse = ChkProgPulse != null ? ChkProgPulse.IsChecked == true : cur.EnableProgressPulse,
            DropdownAnimStyle = (CmbDropdownStyle?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? _currentDropdownAnimStyle ?? cur.DropdownAnimStyle ?? "Scale & Fade",
            TabSwitchTransition = (CmbTabTransition?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? _currentTabSwitchTransition ?? cur.TabSwitchTransition ?? "Slide & Fade",
            EnableStaggeredAnimations = ChkStaggered != null ? ChkStaggered.IsChecked == true : cur.EnableStaggeredAnimations,
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
    }

    private string GetHex(string key)
    {
        if (_colorValues.TryGetValue(key, out var hex) && !string.IsNullOrEmpty(hex) && hex != "#000000")
            return hex;

        var cur = ThemeService.Current;
        return key switch
        {
            "BgDeep"       => cur.BgDeep,
            "BgSurface"    => cur.BgSurface,
            "BgCard"       => cur.BgCard,
            "BgElevated"   => cur.BgElevated,
            "BorderSub"    => cur.BorderSub,
            "Accent"       => cur.Accent,
            "AccentAlt"    => cur.AccentAlt,
            "ButtonGrad"   => cur.ButtonGrad,
            "TextPrimary"  => cur.TextPrimary,
            "TextSec"      => cur.TextSec,
            "SuccessGreen" => cur.SuccessGreen,
            "ErrorRed"     => cur.ErrorRed,
            _              => "#1E1E24"
        };
    }

    // ════════════════════════════════════════════════════════════
    //  PRESETS
    // ════════════════════════════════════════════════════════════
    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        var theme = _presets.FirstOrDefault(p => p.Tag == tag).Theme ?? ThemeService.DefaultDark;
        LoadTheme(theme, applyPreview: true);
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
        => LoadTheme(ThemeService.DefaultDark, applyPreview: true);

    private void ResetAnim_Click(object sender, RoutedEventArgs e)
    {
        _suppressSettingsUpdate = true;

        SelectComboByContent(CmbAnimPreset, "Smooth Liquid");
        UpdatePresetCardSelection("Smooth Liquid");
        if (SliderWinDur != null) SliderWinDur.Value = 380;
        SelectComboByContent(CmbWinEasing, "Apple Spring (Bouncy)");
        SelectComboByContent(CmbWinStyle, "Morph & Scale");

        if (ChkHoverMicro != null) ChkHoverMicro.IsChecked = true;
        if (SliderHoverScale != null) SliderHoverScale.Value = 1.03;
        if (SliderProgSpeed != null) SliderProgSpeed.Value = 350;
        if (ChkProgPulse != null) ChkProgPulse.IsChecked = true;

        SelectComboByContent(CmbTabTransition, "Slide & Fade");
        SelectComboByContent(CmbDropdownStyle, "Scale & Fade");
        if (ChkStaggered != null) ChkStaggered.IsChecked = true;

        _suppressSettingsUpdate = false;

        var theme = ReadTheme();
        ApplyThemeCorrectly(theme);
        Services.ThemeService.Save(theme);
        _originalTheme = theme.Clone();
        IsSaved = true;
    }

    private void SaveAnimOption()
    {
        if (!IsLoaded || _suppressSettingsUpdate || _suppressPickerUpdate) return;

        var theme = ReadTheme();
        ApplyThemeCorrectly(theme);
        Services.ThemeService.Save(theme);
        _originalTheme = theme.Clone();
        IsSaved = true;
    }

    // ════════════════════════════════════════════════════════════
    //  SAVE / CANCEL / CLOSE
    // ════════════════════════════════════════════════════════════
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var theme = ReadTheme();
        ThemeService.Save(theme);
        ApplyThemeCorrectly(theme);
        _originalTheme = theme.Clone();
        _originalSettings = SettingsService.Current.Clone();
        IsSaved = true;

        if (BtnSaveOnly != null)
        {
            BtnSaveOnly.Content = "Saved ✓";
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            timer.Tick += (st, se) =>
            {
                timer.Stop();
                try
                {
                    BtnSaveOnly.SetResourceReference(ContentControl.ContentProperty, "BtnSaveSettings");
                }
                catch
                {
                    BtnSaveOnly.Content = "Save";
                }
            };
            timer.Start();
        }
    }

    private void SaveAndExit_Click(object sender, RoutedEventArgs e)
    {
        var theme = ReadTheme();
        ThemeService.Save(theme);
        ApplyThemeCorrectly(theme);
        _originalTheme = theme.Clone();
        _originalSettings = SettingsService.Current.Clone();
        IsSaved = true;
        CloseWithAnimation(true);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _isExplicitCancel = true;
        CloseWithAnimation(false);
    }

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
        if (_isExplicitCancel)
        {
            ApplyThemeCorrectly(_initialThemeOnOpen);
            Services.ThemeService.Save(_initialThemeOnOpen);
            Services.SettingsService.Save(_initialSettingsOnOpen);
        }
        else if (result != true && !IsSaved)
        {
            ApplyThemeCorrectly(_originalTheme);
            Services.ThemeService.Save(_originalTheme);
            Services.SettingsService.Save(_originalSettings);
        }
        IsSaved = (result == true || IsSaved);
        FluidMotion.MorphClose(RootBorder, WindowScale, WindowTranslate, _originRect, this,
            () => {
                try
                {
                    DialogResult = _pendingResult;
                }
                catch (System.InvalidOperationException)
                {
                    Close();
                }
            });
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

    // ════════════════════════════════════════════════════════════
    //  ANIMATIONS TAB HANDLERS & PLAYGROUND
    // ════════════════════════════════════════════════════════════

    private void PresetCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string preset)
        {
            ApplyMotionProfile(preset);
        }
    }

    private void ApplyMotionProfile(string preset)
    {
        _currentAnimationPreset = preset;
        SelectComboByContent(CmbAnimPreset, preset);
        UpdatePresetCardSelection(preset);

        _suppressSettingsUpdate = true;
        try
        {
            switch (preset)
            {
                case "Disabled (Static)":
                    if (SliderWinDur != null) SliderWinDur.Value = 100;
                    if (SliderHoverScale != null) SliderHoverScale.Value = 1.00;
                    if (SliderProgSpeed != null) SliderProgSpeed.Value = 100;
                    if (ChkHoverMicro != null) ChkHoverMicro.IsChecked = false;
                    if (ChkProgPulse != null) ChkProgPulse.IsChecked = false;
                    if (ChkStaggered != null) ChkStaggered.IsChecked = false;
                    break;
                case "Subtle":
                    if (SliderWinDur != null) SliderWinDur.Value = 200;
                    if (SliderHoverScale != null) SliderHoverScale.Value = 1.015;
                    if (SliderProgSpeed != null) SliderProgSpeed.Value = 250;
                    if (ChkHoverMicro != null) ChkHoverMicro.IsChecked = true;
                    if (ChkProgPulse != null) ChkProgPulse.IsChecked = false;
                    if (ChkStaggered != null) ChkStaggered.IsChecked = true;
                    break;
                case "Balanced":
                    if (SliderWinDur != null) SliderWinDur.Value = 300;
                    if (SliderHoverScale != null) SliderHoverScale.Value = 1.03;
                    if (SliderProgSpeed != null) SliderProgSpeed.Value = 350;
                    if (ChkHoverMicro != null) ChkHoverMicro.IsChecked = true;
                    if (ChkProgPulse != null) ChkProgPulse.IsChecked = true;
                    if (ChkStaggered != null) ChkStaggered.IsChecked = true;
                    break;
                case "Smooth Liquid":
                    if (SliderWinDur != null) SliderWinDur.Value = 380;
                    if (SliderHoverScale != null) SliderHoverScale.Value = 1.03;
                    if (SliderProgSpeed != null) SliderProgSpeed.Value = 350;
                    if (ChkHoverMicro != null) ChkHoverMicro.IsChecked = true;
                    if (ChkProgPulse != null) ChkProgPulse.IsChecked = true;
                    if (ChkStaggered != null) ChkStaggered.IsChecked = true;
                    break;
                case "Hyper Fluid":
                    if (SliderWinDur != null) SliderWinDur.Value = 480;
                    if (SliderHoverScale != null) SliderHoverScale.Value = 1.08;
                    if (SliderProgSpeed != null) SliderProgSpeed.Value = 650;
                    if (ChkHoverMicro != null) ChkHoverMicro.IsChecked = true;
                    if (ChkProgPulse != null) ChkProgPulse.IsChecked = true;
                    if (ChkStaggered != null) ChkStaggered.IsChecked = true;
                    break;
            }
        }
        finally
        {
            _suppressSettingsUpdate = false;
        }

        SaveAnimOption();
    }

    private void UpdatePresetCardSelection(string? preset)
    {
        if (string.IsNullOrEmpty(preset)) preset = "Smooth Liquid";

        Brush accentBrush;
        Brush borderSubBrush;
        try
        {
            accentBrush = (Brush)FindResource("Accent");
            borderSubBrush = (Brush)FindResource("BorderSub");
        }
        catch
        {
            accentBrush = Brushes.DodgerBlue;
            borderSubBrush = Brushes.Gray;
        }

        var cards = new[] {
            (CardPresetStatic, "Disabled (Static)"),
            (CardPresetSubtle, "Subtle"),
            (CardPresetBalanced, "Balanced"),
            (CardPresetLiquid, "Smooth Liquid")
        };

        foreach (var (card, key) in cards)
        {
            if (card == null) continue;
            bool isSelected = string.Equals(preset, key, StringComparison.OrdinalIgnoreCase);
            card.BorderBrush = isSelected ? accentBrush : borderSubBrush;
            card.BorderThickness = isSelected ? new Thickness(2) : new Thickness(1.5);
        }
    }

    private void CmbAnimPreset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettingsUpdate || _suppressPickerUpdate || CmbAnimPreset?.SelectedItem is not ComboBoxItem item) return;
        var preset = item.Content?.ToString() ?? "Balanced";
        ApplyMotionProfile(preset);
    }

    private void SliderWinDur_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtWinDurVal != null) TxtWinDurVal.Text = $"{Math.Round(e.NewValue)} ms";
        if (_suppressSettingsUpdate || _suppressPickerUpdate) return;
        SaveAnimOption();
    }

    private void CmbWinEasing_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettingsUpdate || _suppressPickerUpdate) return;
        SaveAnimOption();
    }

    private void CmbWinStyle_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettingsUpdate || _suppressPickerUpdate) return;
        SaveAnimOption();
    }

    private void ChkHoverMicro_Click(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppressSettingsUpdate || _suppressPickerUpdate) return;
        SaveAnimOption();
    }

    private void SliderHoverScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtHoverScaleVal != null) TxtHoverScaleVal.Text = $"{e.NewValue:0.00}x";
        if (!IsLoaded || _suppressSettingsUpdate || _suppressPickerUpdate) return;
        SaveAnimOption();
    }

    private void SliderProgSpeed_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtProgSpeedVal != null) TxtProgSpeedVal.Text = $"{Math.Round(e.NewValue)} ms";
        if (!IsLoaded || _suppressSettingsUpdate || _suppressPickerUpdate) return;
        SaveAnimOption();
    }

    private void ChkProgPulse_Click(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppressSettingsUpdate || _suppressPickerUpdate) return;
        SaveAnimOption();
    }

    private void CmbTabTransition_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressSettingsUpdate || _suppressPickerUpdate) return;
        SaveAnimOption();
    }

    private void CmbDropdownStyle_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressSettingsUpdate || _suppressPickerUpdate) return;
        SaveAnimOption();
    }

    private void ChkStaggered_Click(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppressSettingsUpdate || _suppressPickerUpdate) return;
        SaveAnimOption();
    }
}
