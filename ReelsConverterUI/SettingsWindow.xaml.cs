using Microsoft.Win32;
using ReelsConverterUI.Animations;
using ReelsConverterUI.Models;
using ReelsConverterUI.Services;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ReelsConverterUI;

public partial class SettingsWindow : Window
{
    private string _lang = "de";
    private readonly Rect _originRect;
    private bool _isAnimatingClose;
    private bool? _pendingResult;
    private StackPanel? _activePanel;
    public bool IsSaved { get; private set; } = false;
    private AppSettings _originalSettings = Services.SettingsService.Current.Clone();

    private readonly Dictionary<string, (double width, double height)> _windowSizes = new();
    private bool _updatingWindowSizeUi;

    public SettingsWindow(Rect originRect)
    {
        InitializeComponent();
        _originRect = originRect;
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (Services.SettingsService.Current.BlurSettings)
        {
            Services.WindowBlurHelper.EnableBlurWithFade(this, RootBorder);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _originalSettings = Services.SettingsService.Current.Clone();
        SettingsService.ApplyWindowSize(this);

        if (Services.SettingsService.Current.BlurSettings)
        {
            Services.WindowBlurHelper.EnableBlurWithFade(this, RootBorder);
            Services.WindowBlurHelper.ApplyRoundedRegion(this);
        }
        FluidMotion.MorphOpen(RootBorder, WindowScale, WindowTranslate, _originRect, this);

        _activePanel = PanelGeneral;
        if (BtnResetWindowSizes != null) BtnResetWindowSizes.Visibility = Visibility.Collapsed;
        if (TopPresetsBar != null) TopPresetsBar.Visibility = Visibility.Collapsed;
        AnimateTabContent(PanelGeneral);

        var s = SettingsService.Current;
        _lang = s.Language;
        if (STxtCurrentLang != null) STxtCurrentLang.Text = _lang.ToUpperInvariant();
        UpdateSLanguageCheckmarks();

        // General
        ChkAlwaysOnTop.IsChecked = s.AlwaysOnTop;
        ChkAutoPaste.IsChecked = s.AutoPasteOnFocus;
        ChkAutoFetch.IsChecked = s.AutoFetchMetadata;
        SelectComboByTag(CmbCompletionNotificationMode, s.CompletionNotificationMode);

        // Window Sizes & Grip
        if (ChkShowWindowResizerGrip != null) ChkShowWindowResizerGrip.IsChecked = s.ShowWindowResizerGrip;
        if (ChkResizerGripOnlyOnHover != null) ChkResizerGripOnlyOnHover.IsChecked = s.ResizerGripOnlyOnHover;
        UpdatePreviewResizeHandleVisibility();

        _windowSizes["main"]     = (s.MainWindowWidth, s.MainWindowHeight);
        _windowSizes["editor"]   = (s.EditorWindowWidth, s.EditorWindowHeight);
        _windowSizes["settings"] = (s.SettingsWindowWidth, s.SettingsWindowHeight);
        _windowSizes["designer"] = (s.DesignerWindowWidth, s.DesignerWindowHeight);
        _windowSizes["desc"]     = (s.DescEditorWindowWidth, s.DescEditorWindowHeight);
        _windowSizes["console"]  = (s.DevConsoleWindowWidth, s.DevConsoleWindowHeight);
        _windowSizes["log"]      = (s.LogViewerWindowWidth, s.LogViewerWindowHeight);

        LoadSelectedWindowSizeUi();

        // Upload
        SelectComboByTag(CmbDefaultPrivacy, s.DefaultPrivacy);
        ChkAutoShorts.IsChecked = s.AutoAddShortsHashtag;
        ChkDefaultFp.IsChecked = s.DefaultFingerprintEnabled;
        SelectComboByTag(CmbDefaultFpMethod, s.DefaultFingerprintMethod);

        // Download
        TxtDefaultOutputDir.Text = s.DefaultOutputDir;
        SelectComboByTag(CmbVideoQuality, s.DefaultVideoQuality);
        ChkDefaultFpDl.IsChecked = s.DefaultFingerprintDlEnabled;
        SelectComboByTag(CmbDefaultFpMethodDl, s.DefaultFingerprintDlMethod);

        // Advanced & Developer
        ChkUseGpu.IsChecked = s.UseGpu;
        SelectComboByTag(CmbMaxJobs, s.MaxConcurrentJobs.ToString());
        if (ChkDeveloperMode != null) ChkDeveloperMode.IsChecked = s.EnableDeveloperMode;
        if (ChkAutoOpenConsoleOnError != null) ChkAutoOpenConsoleOnError.IsChecked = s.AutoOpenConsoleOnError;
        if (ChkVerboseLogging != null) ChkVerboseLogging.IsChecked = s.VerboseLogging;
        if (ChkBypassFileRestrictions != null) ChkBypassFileRestrictions.IsChecked = s.BypassFileRestrictions;
        if (ChkShowPerformanceOverlay != null) ChkShowPerformanceOverlay.IsChecked = s.ShowPerformanceOverlay;

        // Console
        if (ChkDevConsole != null) ChkDevConsole.IsChecked = s.DevConsoleEnabled;
        if (ChkConsoleShowSystem != null) ChkConsoleShowSystem.IsChecked = s.ConsoleShowSystem;
        if (ChkConsoleShowBackend != null) ChkConsoleShowBackend.IsChecked = s.ConsoleShowBackend;
        if (ChkConsoleShowFFmpeg != null) ChkConsoleShowFFmpeg.IsChecked = s.ConsoleShowFFmpeg;

        // Backend
        if (TxtBackendUrl != null) TxtBackendUrl.Text = s.BackendUrl;
        if (TxtBackendTimeout != null) TxtBackendTimeout.Text = s.BackendTimeoutSeconds.ToString();
        if (ChkAutoRestartBackend != null) ChkAutoRestartBackend.IsChecked = s.AutoRestartBackend;
        SelectComboByTag(CmbBackendLogLevel, s.BackendLogLevel);

        UpdateDevSubMenuVisibility();

        // Corner Grip
        SettingsService.SettingsChanged += (_, _) => SettingsService.ApplyResizeGripVisibility(this);
        SettingsService.ApplyResizeGripVisibility(this);
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

    // ═══ Title Bar ═══
    private void TitleBar_Drag(object s, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    // ═══ Language ═══
    private DateTime _sLangPopupClosedTime = DateTime.MinValue;

    private void SLangPopup_Closed(object sender, EventArgs e)
    {
        _sLangPopupClosedTime = DateTime.UtcNow;
    }

    private void SLangPill_Click(object s, RoutedEventArgs e)
    {
        if (SLangPopup == null) return;
        if ((DateTime.UtcNow - _sLangPopupClosedTime).TotalMilliseconds < 250)
        {
            return;
        }

        SLangPopup.PlacementTarget = SBtnLangPill;
        if (!SLangPopup.IsOpen)
        {
            UpdateSLanguageCheckmarks();
            SLangPopup.IsOpen = true;
            AnimatePopupIn(SLangPopupBorder);
        }
        else
        {
            AnimatePopupOut(SLangPopupBorder, () => SLangPopup.IsOpen = false);
        }
    }

    private void SelectSLang_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string lang)
        {
            _lang = lang;
            if (STxtCurrentLang != null) STxtCurrentLang.Text = lang.ToUpperInvariant();
            UpdateSLanguageCheckmarks();

            var dicts = Application.Current.Resources.MergedDictionaries;
            var oldLangs = dicts.Where(d => d.Contains("LangCode")).ToList();
            foreach (var old in oldLangs) dicts.Remove(old);

            var enDict = dicts.FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Strings.en.xaml"));
            if (enDict == null)
            {
                dicts.Insert(0, new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/ReelsConverterUI;component/Assets/Strings.en.xaml")
                });
            }

            if (lang != "en")
            {
                var src = $"Assets/Strings.{lang}.xaml";
                try
                {
                    dicts.Add(new ResourceDictionary
                    {
                        Source = new Uri($"pack://application:,,,/ReelsConverterUI;component/{src}")
                    });
                }
                catch { }
            }

            AnimatePopupOut(SLangPopupBorder, () => SLangPopup.IsOpen = false);
        }
    }

    private void UpdateSLanguageCheckmarks()
    {
        if (SCheckLangDE != null) SCheckLangDE.Visibility = _lang == "de" ? Visibility.Visible : Visibility.Collapsed;
        if (SCheckLangEN != null) SCheckLangEN.Visibility = _lang == "en" ? Visibility.Visible : Visibility.Collapsed;
        if (SCheckLangES != null) SCheckLangES.Visibility = _lang == "es" ? Visibility.Visible : Visibility.Collapsed;
        if (SCheckLangFR != null) SCheckLangFR.Visibility = _lang == "fr" ? Visibility.Visible : Visibility.Collapsed;
        if (SCheckLangIT != null) SCheckLangIT.Visibility = _lang == "it" ? Visibility.Visible : Visibility.Collapsed;
        if (SCheckLangJA != null) SCheckLangJA.Visibility = _lang == "ja" ? Visibility.Visible : Visibility.Collapsed;
        if (SCheckLangZH != null) SCheckLangZH.Visibility = _lang == "zh" ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void AnimatePopupIn(Border border)
    {
        var spring = AppleSpringEase.Interactive;
        var bouncy = AppleSpringEase.Bouncy;
        var smooth = AppleSpringEase.Gentle;
        var group = (TransformGroup)border.RenderTransform;
        var st = (ScaleTransform)group.Children[0];
        var tt = (TranslateTransform)group.Children[1];

        FluidMotion.SetCornerRadiusImmediate(border, 55);

        border.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            { EasingFunction = smooth });
        st.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(420))
            { EasingFunction = spring });
        st.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(420))
            { EasingFunction = spring });
        tt.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(-6, 0, TimeSpan.FromMilliseconds(420))
            { EasingFunction = spring });

        FluidMotion.AnimateCornerRadius(border, 12, TimeSpan.FromMilliseconds(550), bouncy);
    }

    private static void AnimatePopupOut(Border border, Action onDone)
    {
        var ease = AppleSpringEase.Snappy;
        var group = (TransformGroup)border.RenderTransform;
        var st = (ScaleTransform)group.Children[0];
        var tt = (TranslateTransform)group.Children[1];

        FluidMotion.AnimateCornerRadius(border, 55, TimeSpan.FromMilliseconds(160), ease);

        var opAnim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180))
        { EasingFunction = ease };
        opAnim.Completed += (_, _) => onDone();

        border.BeginAnimation(UIElement.OpacityProperty, opAnim);
        st.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.92, TimeSpan.FromMilliseconds(180))
            { EasingFunction = ease });
        st.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.92, TimeSpan.FromMilliseconds(180))
            { EasingFunction = ease });
        tt.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(-6, TimeSpan.FromMilliseconds(180))
            { EasingFunction = ease });
    }

    // ═══ Tab Switching ═══
    private void Tab_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;

        var target = sender == TabGeneral     ? PanelGeneral
                   : sender == TabWindowSizes ? PanelWindowSizes
                   : sender == TabUpload      ? PanelUpload
                   : sender == TabDownload    ? PanelDownload
                   : sender == TabAdvanced    ? PanelAdvanced
                   : sender == TabConsole     ? PanelConsole
                   : sender == TabBackend     ? PanelBackend
                   : sender == TabDevTools    ? PanelDevTools
                   : null;

        if (target != null && target != _activePanel)
        {
            SwitchTab(target);
        }

        UpdateDevSubMenuVisibility(sender);
    }

    private void ChkDeveloperMode_Changed(object sender, RoutedEventArgs e)
    {
        UpdateDevSubMenuVisibility();
    }

    private void UpdateDevSubMenuVisibility(object? currentTabSender = null)
    {
        bool isDevMode = ChkDeveloperMode?.IsChecked == true;
        
        bool isDevTabActive = (currentTabSender != null)
            ? (currentTabSender == TabAdvanced || currentTabSender == TabConsole || currentTabSender == TabBackend || currentTabSender == TabDevTools)
            : (_activePanel == PanelAdvanced || _activePanel == PanelConsole || _activePanel == PanelBackend || _activePanel == PanelDevTools);

        if (PanelAdvancedSubMenu != null)
        {
            PanelAdvancedSubMenu.Visibility = (isDevMode && isDevTabActive) ? Visibility.Visible : Visibility.Collapsed;
        }

        // If Developer Mode is disabled while viewing a Developer sub-tab, return to main Advanced tab
        if (!isDevMode && (_activePanel == PanelConsole || _activePanel == PanelBackend || _activePanel == PanelDevTools))
        {
            if (TabAdvanced != null) TabAdvanced.IsChecked = true;
            if (PanelAdvanced != null) SwitchTab(PanelAdvanced);
        }
    }

    private void SwitchTab(StackPanel target)
    {
        var old = _activePanel;
        if (old == target) return;

        _activePanel = target;

        if (BtnResetWindowSizes != null)
        {
            BtnResetWindowSizes.Visibility = target == PanelWindowSizes ? Visibility.Visible : Visibility.Collapsed;
        }
        if (TopPresetsBar != null)
        {
            bool isResizeTabOpen = (target == PanelWindowSizes && PanelWindowResizeBody?.Visibility == Visibility.Visible);
            if (!isResizeTabOpen)
            {
                TopPresetsBar.Visibility = Visibility.Collapsed;
            }
        }

        if (old != null && old.Visibility == Visibility.Visible)
        {
            var ease = AppleSpringEase.Snappy;
            var dur = TimeSpan.FromMilliseconds(180);
            var fadeOut = new DoubleAnimation(0, dur) { EasingFunction = ease };
            fadeOut.Completed += (_, _) =>
            {
                old.Visibility = Visibility.Collapsed;
            };
            old.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        target.Visibility = Visibility.Visible;
        AnimateTabContent(target);
    }

    private static void AnimateTabContent(StackPanel panel)
    {
        panel.Opacity = 0;
        panel.RenderTransformOrigin = new Point(0.5, 0.15);
        var group = new TransformGroup();
        var st = new ScaleTransform(0.91, 0.88);
        var tt = new TranslateTransform(0, 16);
        group.Children.Add(st);
        group.Children.Add(tt);
        panel.RenderTransform = group;

        var spring = new AppleSpringEase(0.72, 0.44); // Apple fluid app launch spring
        var smooth = AppleSpringEase.Gentle;
        var springDur = TimeSpan.FromMilliseconds(480);

        panel.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            { EasingFunction = smooth });
        tt.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(16, 0, springDur) { EasingFunction = spring });
        st.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.91, 1.0, springDur) { EasingFunction = spring });
        st.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.88, 1.0, springDur) { EasingFunction = spring });

        // Apple Glassmorphic Staggered Child Blossom (Flicker-Free)
        int idx = 0;
        foreach (UIElement child in panel.Children)
        {
            if (child is not FrameworkElement fe) continue;

            fe.Opacity = 0;
            fe.RenderTransformOrigin = new Point(0.5, 0.5);
            var cGroup = new TransformGroup();
            var cSt = new ScaleTransform(0.94, 0.94);
            var cTt = new TranslateTransform(0, 14 + idx * 2);
            cGroup.Children.Add(cSt);
            cGroup.Children.Add(cTt);
            fe.RenderTransform = cGroup;

            var childDur = TimeSpan.FromMilliseconds(240 + idx * 30);
            var childSpring = new AppleSpringEase(0.74, 0.42 + idx * 0.02);

            fe.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200 + idx * 25))
                { EasingFunction = smooth });
            cTt.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(14 + idx * 2, 0, childDur)
                { EasingFunction = childSpring });
            cSt.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.94, 1.0, childDur)
                { EasingFunction = childSpring });
            cSt.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.94, 1.0, childDur)
                { EasingFunction = childSpring });

            idx++;
        }
    }

    // ═══ Window Resize Controls & Live Preview Logic ═══
    private static (double minW, double maxW, double minH, double maxH, double defW, double defH) GetWindowBounds(string tag) => tag switch
    {
        "main"     => (500, 1600, 400, 1200, 760, 560),
        "editor"   => (600, 1920, 400, 1200, 940, 620),
        "settings" => (500, 1200, 400, 900,  680, 500),
        "designer" => (500, 1600, 400, 1000, 840, 540),
        "desc"     => (400, 1400, 300, 1000, 760, 560),
        "console"  => (350, 1200, 200, 800,  520, 320),
        "log"      => (350, 1200, 200, 800,  500, 360),
        _          => (500, 1600, 400, 1200, 760, 560)
    };

    private string GetSelectedWindowTag()
        => (CmbTargetWindow?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "main";

    private void CmbTargetWindow_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        LoadSelectedWindowSizeUi();
    }

    private void LoadSelectedWindowSizeUi()
    {
        var tag = GetSelectedWindowTag();
        var bounds = GetWindowBounds(tag);

        if (!_windowSizes.TryGetValue(tag, out var currentSize))
            currentSize = (bounds.defW, bounds.defH);

        _updatingWindowSizeUi = true;
        try
        {
            SldWidth.Minimum = bounds.minW;
            SldWidth.Maximum = bounds.maxW;
            SldWidth.Value = currentSize.width;
            TxtWidth.Text = Math.Round(currentSize.width).ToString();
            TxtWidthLimits.Text = $"{bounds.minW:F0} - {bounds.maxW:F0}";

            SldHeight.Minimum = bounds.minH;
            SldHeight.Maximum = bounds.maxH;
            SldHeight.Value = currentSize.height;
            TxtHeight.Text = Math.Round(currentSize.height).ToString();
            TxtHeightLimits.Text = $"{bounds.minH:F0} - {bounds.maxH:F0}";
        }
        finally
        {
            _updatingWindowSizeUi = false;
        }

        UpdateLivePreview();
    }

    private void SldWidth_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingWindowSizeUi || !IsLoaded) return;
        var tag = GetSelectedWindowTag();
        var h = _windowSizes.ContainsKey(tag) ? _windowSizes[tag].height : GetWindowBounds(tag).defH;
        _windowSizes[tag] = (e.NewValue, h);

        _updatingWindowSizeUi = true;
        TxtWidth.Text = Math.Round(e.NewValue).ToString();
        _updatingWindowSizeUi = false;

        UpdateLivePreview();
    }

    private void SldHeight_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingWindowSizeUi || !IsLoaded) return;
        var tag = GetSelectedWindowTag();
        var w = _windowSizes.ContainsKey(tag) ? _windowSizes[tag].width : GetWindowBounds(tag).defW;
        _windowSizes[tag] = (w, e.NewValue);

        _updatingWindowSizeUi = true;
        TxtHeight.Text = Math.Round(e.NewValue).ToString();
        _updatingWindowSizeUi = false;

        UpdateLivePreview();
    }

    private void TxtWidth_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingWindowSizeUi || !IsLoaded) return;
        if (double.TryParse(TxtWidth.Text, out var val))
        {
            var tag = GetSelectedWindowTag();
            var bounds = GetWindowBounds(tag);
            val = Math.Clamp(val, bounds.minW, bounds.maxW);

            var h = _windowSizes.ContainsKey(tag) ? _windowSizes[tag].height : bounds.defH;
            _windowSizes[tag] = (val, h);

            _updatingWindowSizeUi = true;
            SldWidth.Value = val;
            _updatingWindowSizeUi = false;

            UpdateLivePreview();
        }
    }

    private void TxtHeight_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingWindowSizeUi || !IsLoaded) return;
        if (double.TryParse(TxtHeight.Text, out var val))
        {
            var tag = GetSelectedWindowTag();
            var bounds = GetWindowBounds(tag);
            val = Math.Clamp(val, bounds.minH, bounds.maxH);

            var w = _windowSizes.ContainsKey(tag) ? _windowSizes[tag].width : bounds.defW;
            _windowSizes[tag] = (w, val);

            _updatingWindowSizeUi = true;
            SldHeight.Value = val;
            _updatingWindowSizeUi = false;

            UpdateLivePreview();
        }
    }

    private void UpdateLivePreview()
    {
        if (PreviewWindowFrame == null) return;
        var tag = GetSelectedWindowTag();
        var bounds = GetWindowBounds(tag);

        if (!_windowSizes.TryGetValue(tag, out var size))
            size = (bounds.defW, bounds.defH);

        double w = size.width;
        double h = size.height;

        // Proportional preview scaling (Canvas height 168, usable height ~110, usable width ~280)
        double scale = Math.Min(260.0 / Math.Max(bounds.maxW, 1000), 102.0 / Math.Max(bounds.maxH, 800));
        double previewW = Math.Clamp(w * scale, 110, 260);
        double previewH = Math.Clamp(h * scale, 62, 108);

        PreviewWindowFrame.Width = previewW;
        PreviewWindowFrame.Height = previewH;

        var selectedTitle = (CmbTargetWindow?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Window";
        PreviewWindowTitle.Text = selectedTitle.Replace("🖥️ ", "").Replace("🎬 ", "").Replace("⚙️ ", "").Replace("🎨 ", "").Replace("📝 ", "").Replace("💻 ", "").Replace("📜 ", "");
        PreviewDimensionText.Text = $"{w:F0} × {h:F0} px";

        // Toggle matching mini preview panel
        if (MiniPanelMain != null) MiniPanelMain.Visibility = tag == "main" ? Visibility.Visible : Visibility.Collapsed;
        if (MiniPanelEditor != null) MiniPanelEditor.Visibility = tag == "editor" ? Visibility.Visible : Visibility.Collapsed;
        if (MiniPanelSettings != null) MiniPanelSettings.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
        if (MiniPanelDesigner != null) MiniPanelDesigner.Visibility = tag == "designer" ? Visibility.Visible : Visibility.Collapsed;
        if (MiniPanelDesc != null) MiniPanelDesc.Visibility = tag == "desc" ? Visibility.Visible : Visibility.Collapsed;
        if (MiniPanelConsole != null) MiniPanelConsole.Visibility = tag == "console" ? Visibility.Visible : Visibility.Collapsed;
        if (MiniPanelLog != null) MiniPanelLog.Visibility = tag == "log" ? Visibility.Visible : Visibility.Collapsed;
    }

    // ═══ Interactive Preview Resize Drag ═══
    private bool _isDraggingPreviewResize;
    private Point _dragStartMousePos;
    private (double width, double height) _dragStartWindowSize;

    private void PreviewResizeHandle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || PreviewCanvasGrid == null) return;
        _isDraggingPreviewResize = true;
        _dragStartMousePos = e.GetPosition(PreviewCanvasGrid);
        var tag = GetSelectedWindowTag();
        if (!_windowSizes.TryGetValue(tag, out _dragStartWindowSize))
        {
            var bounds = GetWindowBounds(tag);
            _dragStartWindowSize = (bounds.defW, bounds.defH);
        }
        PreviewResizeHandle?.CaptureMouse();
        e.Handled = true;
    }

    private void PreviewResizeHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingPreviewResize || PreviewCanvasGrid == null) return;
        var currentPos = e.GetPosition(PreviewCanvasGrid);
        double deltaX = currentPos.X - _dragStartMousePos.X;
        double deltaY = currentPos.Y - _dragStartMousePos.Y;

        var tag = GetSelectedWindowTag();
        var bounds = GetWindowBounds(tag);
        double scale = Math.Min(260.0 / Math.Max(bounds.maxW, 1000), 102.0 / Math.Max(bounds.maxH, 800));

        double newW = Math.Clamp(_dragStartWindowSize.width + (deltaX / scale), bounds.minW, bounds.maxW);
        double newH = Math.Clamp(_dragStartWindowSize.height + (deltaY / scale), bounds.minH, bounds.maxH);

        _windowSizes[tag] = (newW, newH);

        _updatingWindowSizeUi = true;
        SldWidth.Value = newW;
        TxtWidth.Text = Math.Round(newW).ToString();
        SldHeight.Value = newH;
        TxtHeight.Text = Math.Round(newH).ToString();
        _updatingWindowSizeUi = false;

        UpdateLivePreview();
    }

    private void PreviewResizeHandle_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingPreviewResize)
        {
            _isDraggingPreviewResize = false;
            PreviewResizeHandle?.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void SetCurrentSize(double w, double h)
    {
        var tag = GetSelectedWindowTag();
        var bounds = GetWindowBounds(tag);
        w = Math.Clamp(w, bounds.minW, bounds.maxW);
        h = Math.Clamp(h, bounds.minH, bounds.maxH);

        _windowSizes[tag] = (w, h);
        LoadSelectedWindowSizeUi();
    }

    private void PresetCompact_Click(object sender, RoutedEventArgs e)
    {
        var bounds = GetWindowBounds(GetSelectedWindowTag());
        SetCurrentSize(bounds.minW, bounds.minH);
    }

    private void PresetDefault_Click(object sender, RoutedEventArgs e)
    {
        var bounds = GetWindowBounds(GetSelectedWindowTag());
        SetCurrentSize(bounds.defW, bounds.defH);
    }

    private void PresetMedium_Click(object sender, RoutedEventArgs e)
    {
        var bounds = GetWindowBounds(GetSelectedWindowTag());
        SetCurrentSize(bounds.defW * 1.15, bounds.defH * 1.15);
    }

    private void PresetLarge_Click(object sender, RoutedEventArgs e)
    {
        var bounds = GetWindowBounds(GetSelectedWindowTag());
        SetCurrentSize(bounds.maxW * 0.85, bounds.maxH * 0.85);
    }

    private void ResetWindowSizes_Click(object sender, RoutedEventArgs e)
    {
        _windowSizes["main"]     = (760, 560);
        _windowSizes["editor"]   = (940, 620);
        _windowSizes["settings"] = (680, 500);
        _windowSizes["designer"] = (840, 540);
        _windowSizes["desc"]     = (760, 560);
        _windowSizes["console"]  = (520, 320);
        _windowSizes["log"]      = (500, 360);

        LoadSelectedWindowSizeUi();
    }

    private void ChkShowWindowResizerGrip_Click(object sender, RoutedEventArgs e)
    {
        UpdatePreviewResizeHandleVisibility();
    }

    private void ChkResizerGripOnlyOnHover_Click(object sender, RoutedEventArgs e)
    {
        UpdatePreviewResizeHandleVisibility();
    }

    private void UpdatePreviewResizeHandleVisibility()
    {
        if (PreviewResizeHandle == null) return;
        bool show = ChkShowWindowResizerGrip?.IsChecked == true;
        PreviewResizeHandle.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    // ═══ Browse ═══
    private void BrowseOutput_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Default save location" };
        if (dlg.ShowDialog() == true)
            TxtDefaultOutputDir.Text = dlg.FolderName;
    }

    // ═══ Reset ═══
    private void ResetAll_Click(object s, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            L("SettingsResetConfirm"), "ReelsConverter",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        var defaults = new AppSettings();
        SettingsService.Save(defaults);
        SettingsService.ApplyResizeGripVisibility(this);
        SettingsService.ApplyWindowSize(this);
        CloseWithAnimation(true);
    }

    // ═══ Save / Cancel Logic ═══
    private AppSettings BuildSettingsFromUi()
    {
        return new AppSettings
        {
            // General
            Language                     = _lang,
            AlwaysOnTop                  = ChkAlwaysOnTop?.IsChecked == true,
            AutoPasteOnFocus             = ChkAutoPaste?.IsChecked == true,
            AutoFetchMetadata            = ChkAutoFetch?.IsChecked == true,
            CompletionNotificationMode   = (CmbCompletionNotificationMode?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "sound_and_notification",

            // Preserve Theme Designer settings (managed in Theme Designer Layout / Effects tabs)
            AutoShowProgressWindow       = SettingsService.Current.AutoShowProgressWindow,
            HideScrollbars               = SettingsService.Current.HideScrollbars,
            CompactMode                  = SettingsService.Current.CompactMode,
            BlurMainWindow               = SettingsService.Current.BlurMainWindow,
            BlurEditor                   = SettingsService.Current.BlurEditor,
            BlurSettings                 = SettingsService.Current.BlurSettings,
            BlurLogViewer                = SettingsService.Current.BlurLogViewer,
            BlurDevConsole               = SettingsService.Current.BlurDevConsole,
            BlurDescEditor               = SettingsService.Current.BlurDescEditor,

            // Window Sizes & Grip
            ShowWindowResizerGrip        = ChkShowWindowResizerGrip?.IsChecked == true,
            ResizerGripOnlyOnHover       = ChkResizerGripOnlyOnHover?.IsChecked == true,
            MainWindowWidth              = _windowSizes.TryGetValue("main", out var mainS) ? mainS.width : 760,
            MainWindowHeight             = _windowSizes.TryGetValue("main", out mainS) ? mainS.height : 560,
            EditorWindowWidth            = _windowSizes.TryGetValue("editor", out var edS) ? edS.width : 940,
            EditorWindowHeight           = _windowSizes.TryGetValue("editor", out edS) ? edS.height : 620,
            SettingsWindowWidth          = _windowSizes.TryGetValue("settings", out var setS) ? setS.width : 680,
            SettingsWindowHeight         = _windowSizes.TryGetValue("settings", out setS) ? setS.height : 500,
            DesignerWindowWidth          = _windowSizes.TryGetValue("designer", out var desS) ? desS.width : 680,
            DesignerWindowHeight         = _windowSizes.TryGetValue("designer", out desS) ? desS.height : 500,
            DescEditorWindowWidth        = _windowSizes.TryGetValue("desc", out var descS) ? descS.width : 760,
            DescEditorWindowHeight       = _windowSizes.TryGetValue("desc", out descS) ? descS.height : 560,
            DevConsoleWindowWidth        = _windowSizes.TryGetValue("console", out var conS) ? conS.width : 520,
            DevConsoleWindowHeight       = _windowSizes.TryGetValue("console", out conS) ? conS.height : 320,
            LogViewerWindowWidth         = _windowSizes.TryGetValue("log", out var logS) ? logS.width : 500,
            LogViewerWindowHeight        = _windowSizes.TryGetValue("log", out logS) ? logS.height : 360,

            // Upload
            DefaultPrivacy               = (CmbDefaultPrivacy?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "public",
            AutoAddShortsHashtag         = ChkAutoShorts?.IsChecked == true,
            DefaultFingerprintEnabled    = ChkDefaultFp?.IsChecked == true,
            DefaultFingerprintMethod     = (CmbDefaultFpMethod?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "standard",

            // Download
            DefaultOutputDir             = TxtDefaultOutputDir?.Text?.Trim() ?? "",
            DefaultVideoQuality          = (CmbVideoQuality?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "best",
            DefaultFingerprintDlEnabled  = ChkDefaultFpDl?.IsChecked == true,
            DefaultFingerprintDlMethod   = (CmbDefaultFpMethodDl?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "standard",

            // Advanced & Developer
            UseGpu                       = ChkUseGpu?.IsChecked == true,
            MaxConcurrentJobs            = int.TryParse((CmbMaxJobs?.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var mj) ? mj : 1,
            EnableDeveloperMode          = ChkDeveloperMode?.IsChecked == true,
            AutoOpenConsoleOnError       = ChkAutoOpenConsoleOnError?.IsChecked == true,
            VerboseLogging               = ChkVerboseLogging?.IsChecked == true,
            BypassFileRestrictions       = ChkBypassFileRestrictions?.IsChecked == true,
            ShowPerformanceOverlay       = ChkShowPerformanceOverlay?.IsChecked == true,

            // Console
            DevConsoleEnabled            = ChkDevConsole?.IsChecked == true,
            ConsoleShowSystem            = ChkConsoleShowSystem?.IsChecked == true,
            ConsoleShowBackend           = ChkConsoleShowBackend?.IsChecked == true,
            ConsoleShowFFmpeg            = ChkConsoleShowFFmpeg?.IsChecked == true,

            // Backend
            BackendUrl                   = TxtBackendUrl?.Text?.Trim() ?? "http://127.0.0.1:8765",
            BackendTimeoutSeconds        = int.TryParse(TxtBackendTimeout?.Text, out var t) ? t : 30,
            AutoRestartBackend           = ChkAutoRestartBackend?.IsChecked == true,
            BackendLogLevel              = (CmbBackendLogLevel?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "info",
        };
    }

    private void ClearDevCache_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ReelsConverter");
            if (System.IO.Directory.Exists(tempDir))
            {
                System.IO.Directory.Delete(tempDir, true);
            }
            NotificationWindow.Show("Temporary developer cache cleared successfully.", this, NotificationType.Info);
        }
        catch (Exception ex)
        {
            NotificationWindow.Show($"Failed to clear temp cache: {ex.Message}", this, NotificationType.Error);
        }
    }

    private void CopyDevDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var summary = $"ReelsConverter UI Diagnostic Summary\n" +
                          $"OS: {Environment.OSVersion}\n" +
                          $"64-bit Process: {Environment.Is64BitProcess}\n" +
                          $"CLR Version: {Environment.Version}\n" +
                          $"Developer Mode: {ChkDeveloperMode?.IsChecked == true}\n" +
                          $"Console Enabled: {ChkDevConsole?.IsChecked == true}\n" +
                          $"Backend URL: {TxtBackendUrl?.Text}\n" +
                          $"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            Clipboard.SetText(summary);
            NotificationWindow.Show("Diagnostic summary copied to clipboard!", this, NotificationType.Info);
        }
        catch (Exception ex)
        {
            NotificationWindow.Show($"Failed to copy diagnostics: {ex.Message}", this, NotificationType.Error);
        }
    }

    private async void TestBackendConnection_Click(object sender, RoutedEventArgs e)
    {
        var rawUrl = TxtBackendUrl?.Text?.Trim();
        if (string.IsNullOrEmpty(rawUrl)) rawUrl = "http://127.0.0.1:8765";

        var baseUrl = rawUrl.TrimEnd('/');
        var healthUrl = baseUrl.EndsWith("/api/health") ? baseUrl : $"{baseUrl}/api/health";

        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            var response = await client.GetAsync(healthUrl);
            if (response.IsSuccessStatusCode)
            {
                NotificationWindow.Show("Backend API service is online and healthy!", this, NotificationType.Info);
            }
            else
            {
                NotificationWindow.Show($"Backend service responded with HTTP status {(int)response.StatusCode} ({response.StatusCode}).", this, NotificationType.Warning);
            }
        }
        catch (Exception ex)
        {
            NotificationWindow.Show($"Could not connect to backend service at {baseUrl}.\nError: {ex.Message}", this, NotificationType.Error);
        }
    }

    private void ApplyBlurToOpenWindows(AppSettings s)
    {
        try
        {
            if (s.BlurSettings) WindowBlurHelper.EnableBlurWithFade(this, RootBorder);
            else WindowBlurHelper.DisableBlur(this);

            if (Owner is Window win && win.FindName("RootBorder") is Border border)
            {
                if (s.BlurMainWindow) WindowBlurHelper.EnableBlurWithFade(win, border);
                else WindowBlurHelper.DisableBlur(win);
            }
        }
        catch { }
    }

    private void SaveOnly_Click(object s, RoutedEventArgs e)
    {
        var settings = BuildSettingsFromUi();
        SettingsService.Save(settings);
        _originalSettings = settings.Clone();

        SettingsService.ApplyWindowSize(this);
        if (Owner != null)
            SettingsService.ApplyWindowSize(Owner);

        ApplyBlurToOpenWindows(settings);

        IsSaved = true;

        if (BtnSaveOnly != null)
        {
            BtnSaveOnly.Content = "Saved ✓";
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            timer.Tick += (st, se) =>
            {
                timer.Stop();
                BtnSaveOnly.Content = L("BtnSaveOnly");
            };
            timer.Start();
        }
    }

    private void SaveAndExit_Click(object s, RoutedEventArgs e)
    {
        var settings = BuildSettingsFromUi();
        SettingsService.Save(settings);
        _originalSettings = settings.Clone();

        SettingsService.ApplyWindowSize(this);
        if (Owner != null)
            SettingsService.ApplyWindowSize(Owner);

        ApplyBlurToOpenWindows(settings);

        CloseWithAnimation(true);
    }

    private void Cancel_Click(object s, RoutedEventArgs e) => CloseWithAnimation(false);

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
        if (result != true)
        {
            SettingsService.Save(_originalSettings);
            SettingsService.ApplyResizeGripVisibility(this);
            ApplyBlurToOpenWindows(_originalSettings);
        }
        IsSaved = (result == true);
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

    private void HeaderWindowResize_Click(object sender, MouseButtonEventArgs e)
    {
        if (PanelWindowResizeBody == null || RotWindowResizeArrow == null || TransWindowResizeBody == null) return;

        var ease = AppleSpringEase.Interactive;
        var dur = TimeSpan.FromMilliseconds(220);

        if (PanelWindowResizeBody.Visibility == Visibility.Visible)
        {
            // Collapse Animation
            RotWindowResizeArrow.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)) { EasingFunction = AppleSpringEase.Snappy });

            var opAnim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = AppleSpringEase.Snappy };
            opAnim.Completed += (s, args) => PanelWindowResizeBody.Visibility = Visibility.Collapsed;
            PanelWindowResizeBody.BeginAnimation(UIElement.OpacityProperty, opAnim);

            TransWindowResizeBody.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, -6, TimeSpan.FromMilliseconds(180)) { EasingFunction = AppleSpringEase.Snappy });

            AnimatePresetsPopOut(TopPresetsBar);
        }
        else
        {
            // Expand Animation
            PanelWindowResizeBody.Visibility = Visibility.Visible;

            RotWindowResizeArrow.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(180, dur) { EasingFunction = ease });

            PanelWindowResizeBody.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, dur) { EasingFunction = ease });

            TransWindowResizeBody.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(-6, 0, dur) { EasingFunction = ease });

            AnimatePresetsPopIn(TopPresetsBar);
        }
    }

    private void AnimatePresetsPopIn(FrameworkElement? presetsBar)
    {
        if (presetsBar == null) return;
        presetsBar.Visibility = Visibility.Visible;
        presetsBar.Opacity = 0;

        var bouncy = AppleSpringEase.Bouncy;
        var dur = TimeSpan.FromMilliseconds(420);

        if (ScalePresetsBar != null)
        {
            ScalePresetsBar.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.75, 1.0, dur) { EasingFunction = bouncy });
            ScalePresetsBar.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.75, 1.0, dur) { EasingFunction = bouncy });
        }
        if (TransPresetsBar != null)
        {
            TransPresetsBar.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(-14, 0, dur) { EasingFunction = bouncy });
        }

        presetsBar.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = AppleSpringEase.Gentle });
    }

    private void AnimatePresetsPopOut(FrameworkElement? presetsBar)
    {
        if (presetsBar == null || presetsBar.Visibility == Visibility.Collapsed) return;

        var snappy = AppleSpringEase.Snappy;
        var dur = TimeSpan.FromMilliseconds(180);

        if (ScalePresetsBar != null)
        {
            ScalePresetsBar.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.8, dur) { EasingFunction = snappy });
            ScalePresetsBar.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.8, dur) { EasingFunction = snappy });
        }
        if (TransPresetsBar != null)
        {
            TransPresetsBar.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(-8, dur) { EasingFunction = snappy });
        }

        var fadeOut = new DoubleAnimation(0, dur) { EasingFunction = snappy };
        fadeOut.Completed += (s, args) => presetsBar.Visibility = Visibility.Collapsed;
        presetsBar.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
            if (item.Tag?.ToString() == tag) { combo.SelectedItem = item; return; }
    }

    private static string L(string key)
        => Application.Current.Resources[key] as string ?? key;
}
