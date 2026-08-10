using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using ReelsConverterUI.Animations;
using ReelsConverterUI.Models;
using ReelsConverterUI.Services;

namespace ReelsConverterUI;

public partial class MainWindow : Window
{
    private readonly BackendLauncher _launcher = new();
    private readonly BackendService _backend = new(SettingsService.Current.BackendUrl);
    private MetadataResponse? _meta;
    private CancellationTokenSource? _cts;
    private string? _currentJobId;
    private ProgressWindow? _progressWin;
    private LogViewerWindow? _logViewer;
    private DevConsoleWindow? _devConsoleWin;
    private string _lastJobLog = string.Empty;
    private string _lastLogEntry = string.Empty;
    private bool _backendReady;
    private string _currentLang = "de";
    private bool _devConsoleCollapsed;
    private string? _lastDownloadedFolder;
    private string? _lastDownloadedFile;
    private readonly List<string> _inlineCmdHistory = new();
    private int _inlineCmdHistoryIndex = -1;
    private Color? _lastDominantColor;
    internal Color? LastDominantColor => _lastDominantColor;
    private bool _isLoadingFromDrag;

    // ── Segment pill drag state (iOS 26 interactive tracking) ──
    private bool _pillDragPending;
    private bool _pillDragging;
    private bool _pillSnapFromDrag;
    private double _pillDragAnchor;
    private double _pillLastMoveX;
    private DateTime _pillLastMoveTime;

    private bool _isAnimatingClose;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closing += MainWindow_Closing;
        SizeChanged += (_, _) => UpdateWindowGradient();
        ThemeService.ThemeApplied += OnThemeApplied;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (SettingsService.Current.BlurMainWindow)
        {
            Services.WindowBlurHelper.EnableBlurWithFade(this, RootBorder);
        }
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

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        ThemeService.ThemeApplied -= OnThemeApplied;
        if (!_isAnimatingClose)
        {
            e.Cancel = true;
            CloseWithAnimation();
        }
    }

    private void OnThemeApplied()
    {
        RefreshDominantColorVisuals();
        if (ThemeService.Current.DisableThumbnailCard)
            BorderMeta.Visibility = Visibility.Collapsed;
        else if (_meta != null)
            BorderMeta.Visibility = Visibility.Visible;
    }

    private void CloseWithAnimation()
    {
        if (_isAnimatingClose) return;
        _isAnimatingClose = true;

        // Cleanup resources before window hides
        _cts?.Cancel();
        _devConsoleWin?.Close();
        _launcher.Dispose();
        _backend.Dispose();

        var ease = AppleSpringEase.Snappy;
        var dur = TimeSpan.FromMilliseconds(250);

        var opAnim = new DoubleAnimation(1, 0, dur) { EasingFunction = ease };
        opAnim.Completed += (s, e) => Close();

        RootBorder.BeginAnimation(UIElement.OpacityProperty, opAnim);
        WindowScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, 0.88, dur) { EasingFunction = ease });
        WindowScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, 0.85, dur) { EasingFunction = ease });
    }

    // ════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ════════════════════════════════════════════════════════════
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        SettingsService.SettingsChanged += (_, _) => SettingsService.ApplyResizeGripVisibility(this);
        SettingsService.ApplyResizeGripVisibility(this);
        ThemeService.ThemeApplied += () => Dispatcher.Invoke(RefreshDominantColorVisuals);
        ApplySettings();
        FluidMotion.SetCornerRadiusValue(SegmentIndicator, 50);
        Mode_Changed(sender, e);

        if (SettingsService.Current.BlurMainWindow)
        {
            Services.WindowBlurHelper.EnableBlurWithFade(this, RootBorder);
            Services.WindowBlurHelper.ApplyRoundedRegion(this);
        }

        Activated += (_, _) =>
        {
            if (SettingsService.Current.AutoPasteOnFocus
                && Clipboard.ContainsText()
                && string.IsNullOrWhiteSpace(TxtUrl.Text))
            {
                TxtUrl.Text = Clipboard.GetText().Trim();
            }
        };

        try
        {
            _launcher.OutputReceived += line =>
                Dispatcher.BeginInvoke(() => DevLog($"[backend] {line}"));
            _launcher.Start();
            DevLog("Backend launcher started, waiting for health check...");
            _backendReady = await _backend.WaitForHealthAsync(
                CancellationToken.None, SettingsService.Current.BackendTimeoutSeconds);
            DevLog(_backendReady ? "Backend health check: OK" : "Backend health check: FAILED");
            SetStatus(_backendReady ? L("StatusBackendReady") : L("StatusBackendDown"),
                      _backendReady);
        }
        catch (Exception ex)
        {
            SetStatus($"{L("StatusBackendErrPrefix")} {ex.Message}", false);
        }
    }

    // ════════════════════════════════════════════════════════════
    //  TITLEBAR
    // ════════════════════════════════════════════════════════════
    private void TitleBar_Drag(object s, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void Minimize_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object s, RoutedEventArgs e) => Close();
    private void Help_Click(object s, RoutedEventArgs e)
    {
        HelpPopup.PlacementTarget = (UIElement)s;
        if (!HelpPopup.IsOpen)
        {
            HelpPopup.IsOpen = true;
            AnimatePopupIn(HelpPopupBorder);
        }
        else
        {
            AnimatePopupOut(HelpPopupBorder, () => HelpPopup.IsOpen = false);
        }
    }

    // ════════════════════════════════════════════════════════════
    //  MODE TOGGLE
    // ════════════════════════════════════════════════════════════
    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;

        bool toDownload = RbDownload?.IsChecked == true;
        var targetPanel = toDownload ? BorderDownload : BorderUpload;
        var otherPanel = toDownload ? BorderUpload : BorderDownload;

        if (targetPanel.Visibility == Visibility.Visible && targetPanel.Opacity >= 0.95 && otherPanel.Visibility == Visibility.Collapsed)
        {
            return;
        }

        // ── Pill indicator: animated only for taps (drag handles its own pill) ──
        if (!_pillSnapFromDrag && SegmentIndicator != null && RbUpload.ActualWidth > 0)
        {
            double targetX = toDownload ? RbUpload.ActualWidth : 0;
            var spring = AppleSpringEase.Interactive;
            var smooth = AppleSpringEase.Smooth;
            var gentle = AppleSpringEase.Gentle;
            var totalDur = TimeSpan.FromMilliseconds(680);

            // Opacity: solid → deep glassy → long hold → slow recovery
            var opAnim = new DoubleAnimationUsingKeyFrames { Duration = totalDur };
            opAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0,  KeyTime.FromPercent(0)));
            opAnim.KeyFrames.Add(new EasingDoubleKeyFrame(0.25, KeyTime.FromPercent(0.10)) { EasingFunction = gentle });
            opAnim.KeyFrames.Add(new EasingDoubleKeyFrame(0.25, KeyTime.FromPercent(0.65)));
            opAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0,  KeyTime.FromPercent(1.0))  { EasingFunction = gentle });
            SegmentIndicator.BeginAnimation(UIElement.OpacityProperty, opAnim);

            // ScaleX: wide liquid horizontal stretch + bouncy spring settle
            var bouncy = AppleSpringEase.Bouncy;
            var scaleXAnim = new DoubleAnimationUsingKeyFrames { Duration = totalDur };
            scaleXAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0,  KeyTime.FromPercent(0)));
            scaleXAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.28, KeyTime.FromPercent(0.16)) { EasingFunction = gentle });
            scaleXAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.28, KeyTime.FromPercent(0.35)));
            scaleXAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0,  KeyTime.FromPercent(1.0))  { EasingFunction = bouncy });
            SegmentScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);

            // ScaleY: slight vertical compress during horizontal stretch
            var scaleYAnim = new DoubleAnimationUsingKeyFrames { Duration = totalDur };
            scaleYAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0,  KeyTime.FromPercent(0)));
            scaleYAnim.KeyFrames.Add(new EasingDoubleKeyFrame(0.94, KeyTime.FromPercent(0.18)) { EasingFunction = gentle });
            scaleYAnim.KeyFrames.Add(new EasingDoubleKeyFrame(0.94, KeyTime.FromPercent(0.40)));
            scaleYAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0,  KeyTime.FromPercent(1.0))  { EasingFunction = spring });
            SegmentScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);

            // Slide: delayed start so glassy+scale happen first
            SegmentTranslateX.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(targetX, TimeSpan.FromMilliseconds(500))
                { BeginTime = TimeSpan.FromMilliseconds(140), EasingFunction = spring });

            // CornerRadius: capsule → softer blob → reform capsule with bounce
            FluidMotion.SetCornerRadiusValue(SegmentIndicator, 50);
            var crAnim = new DoubleAnimationUsingKeyFrames { Duration = totalDur };
            crAnim.KeyFrames.Add(new EasingDoubleKeyFrame(50, KeyTime.FromPercent(0)));
            crAnim.KeyFrames.Add(new EasingDoubleKeyFrame(28, KeyTime.FromPercent(0.12)) { EasingFunction = gentle });
            crAnim.KeyFrames.Add(new EasingDoubleKeyFrame(28, KeyTime.FromPercent(0.60)));
            crAnim.KeyFrames.Add(new EasingDoubleKeyFrame(50, KeyTime.FromPercent(1.0))  { EasingFunction = bouncy });
            FluidMotion.AnimateCornerRadiusKeyFrames(SegmentIndicator, crAnim);
        }
        _pillSnapFromDrag = false;

        // ── Content panels: liquid glass crossfade (always runs) ──
        var showPanel = toDownload ? BorderDownload : BorderUpload;
        var hidePanel = toDownload ? BorderUpload : BorderDownload;
        double slideDir = toDownload ? 1.0 : -1.0;

        FluidMotion.LiquidGlassCrossfade(hidePanel, showPanel, slideDir);

        // Show/hide open-folder button with mode switch
        if (toDownload && !string.IsNullOrEmpty(_lastDownloadedFolder))
            ShowOpenFolderBar();
        else
            HideOpenFolderBar();
    }

    // ════════════════════════════════════════════════════════════
    //  SEGMENT PILL DRAG  (iOS 26 liquid glass interactive)
    // ════════════════════════════════════════════════════════════
    private void Segment_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (SegmentIndicator == null || RbUpload.ActualWidth <= 0) return;
        var grid = (UIElement)SegmentIndicator.Parent;
        double mouseX = e.GetPosition(grid).X;

        _pillDragPending = true;
        _pillDragging = false;
        _pillLastMoveX = mouseX;
        _pillLastMoveTime = DateTime.UtcNow;
        _pillDragAnchor = SegmentIndicator.ActualWidth / 2.0;
    }

    private void Segment_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_pillDragPending) return;
        var grid = (UIElement)SegmentIndicator.Parent;
        double mouseX = e.GetPosition(grid).X;

        if (!_pillDragging)
        {
            if (Math.Abs(mouseX - _pillLastMoveX) < 5) return;
            _pillDragging = true;

            // Capture mouse now that we know it's a drag
            ((UIElement)sender).CaptureMouse();

            // Cancel running animations and enter liquid state
            SegmentTranslateX.BeginAnimation(TranslateTransform.XProperty, null);
            SegmentScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            SegmentScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            SegmentIndicator.BeginAnimation(UIElement.OpacityProperty, null);

            SegmentIndicator.Opacity = 0.28;
            SegmentScale.ScaleX = 1.18;
            SegmentScale.ScaleY = 0.95;

            // Liquid blob corners (softer than capsule during drag)
            FluidMotion.SetCornerRadiusImmediate(SegmentIndicator, 30);
        }

        // Velocity for liquid deformation
        var now = DateTime.UtcNow;
        double dt = Math.Max((now - _pillLastMoveTime).TotalSeconds, 0.001);
        double velocity = (mouseX - _pillLastMoveX) / dt;
        _pillLastMoveX = mouseX;
        _pillLastMoveTime = now;

        // Track pill position with rubber-band at edges
        double maxX = RbUpload.ActualWidth;
        double raw = mouseX - _pillDragAnchor;
        double clamped;
        if (raw < 0)
            clamped = raw * 0.25;
        else if (raw > maxX)
            clamped = maxX + (raw - maxX) * 0.25;
        else
            clamped = raw;
        SegmentTranslateX.X = clamped;

        // Liquid squash-and-stretch: velocity elongates X, compresses Y
        double vFactor = Math.Clamp(Math.Abs(velocity) / 500.0, 0, 0.14);
        SegmentScale.ScaleX = 1.18 + vFactor;
        SegmentScale.ScaleY = 0.95 - vFactor * 0.5;

        // Velocity softens corners (30 base → down to 22 at max speed for blobby feel)
        double crVelocity = Math.Clamp(Math.Abs(velocity) / 600.0, 0, 1.0);
        FluidMotion.SetCornerRadiusImmediate(SegmentIndicator, 30 - crVelocity * 8);
    }

    private void Segment_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        bool wasDragging = _pillDragging;
        _pillDragPending = false;
        _pillDragging = false;

        if (!wasDragging) return; // was a tap — let RadioButton handle it

        ((UIElement)sender).ReleaseMouseCapture();
        e.Handled = true;

        // Spring-snap to nearest segment
        double currentX = SegmentTranslateX.X;
        double colW = RbUpload.ActualWidth;
        bool snapToDownload = currentX > colW * 0.5;
        double targetX = snapToDownload ? colW : 0;

        var spring = AppleSpringEase.Interactive;
        var smooth = AppleSpringEase.Smooth;

        var gentle = AppleSpringEase.Gentle;
        var bouncy = AppleSpringEase.Bouncy;

        SegmentTranslateX.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(currentX, targetX, TimeSpan.FromMilliseconds(480))
            { EasingFunction = spring });
        SegmentScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(SegmentScale.ScaleX, 1.0, TimeSpan.FromMilliseconds(580))
            { EasingFunction = bouncy });
        SegmentScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(SegmentScale.ScaleY, 1.0, TimeSpan.FromMilliseconds(520))
            { EasingFunction = gentle });
        SegmentIndicator.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0.28, 1.0, TimeSpan.FromMilliseconds(420))
            { EasingFunction = gentle });

        // Reform corners: liquid blob → capsule shape with bouncy spring
        FluidMotion.AnimateCornerRadius(SegmentIndicator, 50,
            TimeSpan.FromMilliseconds(580), bouncy);

        // Switch content if segment changed
        bool wasDownload = RbDownload.IsChecked == true;
        if (snapToDownload != wasDownload)
        {
            _pillSnapFromDrag = true;
            if (snapToDownload)
                RbDownload.IsChecked = true;
            else
                RbUpload.IsChecked = true;
        }
    }

    // ════════════════════════════════════════════════════════════
    //  PASTE & BROWSE
    // ════════════════════════════════════════════════════════════
    private void Paste_Click(object s, RoutedEventArgs e)
    {
        if (Clipboard.ContainsText())
            TxtUrl.Text = Clipboard.GetText().Trim();
    }

    private List<ComboBoxItem>? _videoQualityItems;
    private List<ComboBoxItem>? _ytmusicQualityItems;
    private bool? _isYTMusicActiveState = null;

    private void EnsureQualityItemLists()
    {
        if (_videoQualityItems == null && CmbItemBest != null && CmbItem1080 != null && CmbItem720 != null && CmbItem480 != null && CmbItem360 != null && CmbItemAudio != null)
        {
            _videoQualityItems = new List<ComboBoxItem>
            {
                CmbItemBest, CmbItem1080, CmbItem720, CmbItem480, CmbItem360, CmbItemAudio
            };
        }
        if (_ytmusicQualityItems == null && CmbItemYt320 != null && CmbItemYtM4a != null && CmbItemYtFlac != null)
        {
            _ytmusicQualityItems = new List<ComboBoxItem>
            {
                CmbItemYt320, CmbItemYtM4a, CmbItemYtFlac
            };
        }
    }

    private void UpdateQualityOptionsVisibility(bool isYTMusic)
    {
        if (CmbQuality == null) return;
        EnsureQualityItemLists();

        if (_isYTMusicActiveState == isYTMusic) return;
        _isYTMusicActiveState = isYTMusic;

        CmbQuality.Items.Clear();

        if (isYTMusic && _ytmusicQualityItems != null)
        {
            foreach (var item in _ytmusicQualityItems)
            {
                item.Visibility = Visibility.Visible;
                CmbQuality.Items.Add(item);
            }
            CmbQuality.SelectedItem = CmbItemYt320;
        }
        else if (_videoQualityItems != null)
        {
            foreach (var item in _videoQualityItems)
            {
                item.Visibility = Visibility.Visible;
                CmbQuality.Items.Add(item);
            }
            CmbQuality.SelectedItem = CmbItemBest;
        }
    }

    private void TxtUrl_TextChanged(object sender, TextChangedEventArgs e)
    {
        var url = TxtUrl.Text.Trim().ToLowerInvariant();
        string? platform = null;
        bool isYTMusic = url.Contains("music.youtube.com");
        UpdateQualityOptionsVisibility(isYTMusic);

        if (isYTMusic)
            platform = "YouTube Music";
        else if (url.Contains("instagram.com") || url.Contains("instagr.am"))
            platform = "Instagram";
        else if (url.Contains("tiktok.com") || url.Contains("vm.tiktok"))
            platform = "TikTok";
        else if (url.Contains("youtube.com") || url.Contains("youtu.be"))
            platform = "YouTube";
        else if (url.Contains("twitter.com") || url.Contains("x.com"))
            platform = "X / Twitter";
        else if (url.Contains("facebook.com") || url.Contains("fb.watch"))
            platform = "Facebook";
        else if (!string.IsNullOrEmpty(url) && url.StartsWith("http"))
            platform = "Other";

        if (platform != null)
            DevLog($"Platform detected: {platform}");
    }

    private void Browse_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Speicherort wählen" };
        if (dlg.ShowDialog() == true)
            TxtOutputDir.Text = dlg.FolderName;
    }

    private void EditDescription_Click(object s, RoutedEventArgs e)
    {
        foreach (Window w in OwnedWindows)
        {
            if (w is DescriptionEditorWindow existing)
            {
                existing.Focus();
                return;
            }
        }

        var editor = new DescriptionEditorWindow(TxtDescription.Text, GetBtnRect((UIElement)s)) { Owner = this };
        editor.Closed += (sender, args) =>
        {
            if (editor.IsSaved)
                TxtDescription.Text = editor.Description;
        };
        editor.Show();
    }

    // ════════════════════════════════════════════════════════════
    //  FETCH METADATA
    // ════════════════════════════════════════════════════════════
    private async void Fetch_Click(object s, RoutedEventArgs e)
    {
        await FetchMetadataAsync(TxtUrl.Text.Trim());
    }

    private async System.Threading.Tasks.Task FetchMetadataAsync(string url)
    {
        if (string.IsNullOrEmpty(url)) { Warn(L("ErrNoUrl")); return; }
        if (!_backendReady) { Warn(L("ErrNoBackend")); return; }

        BtnFetch.IsEnabled = false;
        SetStatus(L("StatusLoading"), true);

        try
        {
            DevLog($"Fetching metadata for: {url}");
            if (url.ToLowerInvariant().Contains("music.youtube.com"))
                _meta = await _backend.FetchYTMusicMetadataAsync(url);
            else
                _meta = await _backend.FetchMetadataAsync(url);

            if (_meta is null) { Warn(L("ErrNoMeta")); return; }

            TxtMetaTitle.Text = _meta.Title;
            var uploaderName = !string.IsNullOrEmpty(_meta.Artist) ? _meta.Artist : _meta.Uploader;
            TxtMetaUploader.Text = $"👤 {uploaderName}";
            TxtMetaDuration.Text = $"⏱ {TimeSpan.FromSeconds(_meta.Duration):mm\\:ss}";
            TagsList.ItemsSource = _meta.Tags.Take(8).Select(t => $"#{t}").ToList();
            LoadThumbnail(_meta.Thumbnail);

            bool isYTMusic = _meta.IsMusic || url.ToLowerInvariant().Contains("music.youtube.com");
            UpdateQualityOptionsVisibility(isYTMusic);

            if (isYTMusic)
            {
                BorderYTMusicBadge.Visibility = Visibility.Visible;
                if (!string.IsNullOrEmpty(_meta.Album))
                {
                    TxtMetaAlbum.Text = $"💿 {_meta.Album}";
                    TxtMetaAlbum.Visibility = Visibility.Visible;
                }
                else
                {
                    TxtMetaAlbum.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                BorderYTMusicBadge.Visibility = Visibility.Collapsed;
                TxtMetaAlbum.Visibility = Visibility.Collapsed;
            }

            TxtTitle.Text = _meta.Title;
            TxtTitleDl.Text = _meta.Title;
            TxtDescription.Text = _meta.Description;
            DevLog($"Metadata loaded: \"{_meta.Title}\" by {uploaderName} ({_meta.Duration:F0}s, {_meta.Tags.Count} tags)");

            AnimatePanel(BorderMeta, true);
            SetStatus(L("StatusMetaLoaded"), true);
        }
        catch (Exception ex)
        {
            DevLog($"Error fetching metadata: {ex.Message}");
            Warn($"{L("ErrPrefix")} {ex.Message}");
            SetStatus(L("StatusLoadErr"), false);
        }
        finally
        {
            BtnFetch.IsEnabled = true;
        }
    }

    private void LoadThumbnail(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            ImgThumbnail.Source = null;
            TxtMetaThumbFallback.Visibility = Visibility.Visible;
            ResetThumbBackground();
            return;
        }

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(url, UriKind.Absolute);
            bmp.DecodePixelWidth = 400;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();

            if (bmp.IsDownloading || bmp.PixelWidth <= 1)
            {
                bmp.DownloadCompleted += (_, _) => {
                    if (bmp.PixelWidth > 1) ApplyDominantColor(bmp);
                };
            }
            else
            {
                ApplyDominantColor(bmp);
            }

            ImgThumbnail.Source = bmp;
            TxtMetaThumbFallback.Visibility = Visibility.Collapsed;
        }
        catch
        {
            ImgThumbnail.Source = null;
            TxtMetaThumbFallback.Visibility = Visibility.Visible;
            ResetThumbBackground();
        }
    }

    private void RefreshDominantColorVisuals()
    {
        var mode = ThemeService.Current?.GradientEffectMode ?? "thumbnail_only";
        if (mode == "none")
        {
            ResetThumbBackground();
            return;
        }

        if (_lastDominantColor is Color color)
        {
            var baseCardObj = FindResource("InputBg");
            var baseCard = baseCardObj is SolidColorBrush scb ? scb.Color : Color.FromArgb(0x0A, 255, 255, 255);
            byte cardAlpha = baseCard.A;

            // Gradient: subtle dominant tint at top fading to base InputBg at bottom while preserving glass material transparency
            const double a = 0.25;
            var cardTint = Color.FromArgb(
                (byte)Math.Max((int)cardAlpha, 0x1A),
                (byte)(baseCard.R * (1 - a) + color.R * a),
                (byte)(baseCard.G * (1 - a) + color.G * a),
                (byte)(baseCard.B * (1 - a) + color.B * a));

            DevLog($"Card tint calculated: {cardTint}, Base card: {baseCard}");

            var gradient = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0),
                EndPoint = new Point(0.5, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(cardTint, 0.0),
                    new GradientStop(baseCard, 1.0)
                }
            };
            BorderMeta.Background = gradient;

            if (mode == "both")
            {
                // Measure position and update window gradient
                bool wasCollapsed = BorderMeta.Visibility == Visibility.Collapsed;
                if (wasCollapsed)
                {
                    BorderMeta.Visibility = Visibility.Visible;
                }

                RootBorder.UpdateLayout();
                UpdateWindowGradient();

                if (wasCollapsed)
                {
                    BorderMeta.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                RootBorder.SetResourceReference(Border.BackgroundProperty, "BgDeep");
            }

            // Subtle tint for the thumbnail container (visible in letterbox gaps)
            const double b = 0.30;
            var thumbBg = Color.FromArgb(
                (byte)Math.Max((int)cardAlpha, 0x22),
                (byte)(baseCard.R * (1 - b) + color.R * b),
                (byte)(baseCard.G * (1 - b) + color.G * b),
                (byte)(baseCard.B * (1 - b) + color.B * b));

            ThumbBgBorder.Background = new SolidColorBrush(thumbBg);
        }
        else
        {
            ResetThumbBackground();
        }
    }

    private void ApplyDominantColor(BitmapSource bmp)
    {
        try
        {
            DevLog($"ApplyDominantColor: source size = {bmp.PixelWidth}x{bmp.PixelHeight}");
            var color = GetDominantColor(bmp);
            DevLog($"Dominant color extracted: {color}");
            _lastDominantColor = color;

            if (ThemeService.Current.AdaptiveThumbnailTheme)
            {
                ThemeService.Apply(CreateAdaptiveTheme(ThemeService.Current, color));
            }
            else
            {
                ThemeService.Apply(ThemeService.Current);
            }
        }
        catch (Exception ex)
        {
            DevLog($"Dominant color extraction failed: {ex.Message}");
        }
    }

    private void ResetThumbBackground()
    {
        BorderMeta.SetResourceReference(Border.BackgroundProperty, "InputBg");
        ThumbBgBorder.SetResourceReference(Border.BackgroundProperty, "BgElevated");

        _lastDominantColor = null;
        UpdateWindowGradient();
    }

    private void UpdateWindowGradient()
    {
        if (_lastDominantColor is Color color)
        {
            double relativeX = 0.255;
            double relativeY = 0.403;
            try
            {
                if (BorderMeta.Visibility == Visibility.Visible &&
                    ThumbBgBorder.ActualWidth > 0 && ThumbBgBorder.ActualHeight > 0 &&
                    RootBorder.ActualWidth > 0 && RootBorder.ActualHeight > 0)
                {
                    var centerInThumb = new Point(ThumbBgBorder.ActualWidth / 2, ThumbBgBorder.ActualHeight / 2);
                    var centerInRoot = ThumbBgBorder.TransformToAncestor(RootBorder).Transform(centerInThumb);
                    relativeX = centerInRoot.X / RootBorder.ActualWidth;
                    relativeY = centerInRoot.Y / RootBorder.ActualHeight;
                }
            }
            catch (Exception ex)
            {
                DevLog($"Failed to compute relative thumbnail position: {ex.Message}");
            }

            double w = RootBorder.ActualWidth > 0 ? RootBorder.ActualWidth : 760;
            double h = RootBorder.ActualHeight > 0 ? RootBorder.ActualHeight : 560;

            double radiusPixels = Math.Max(w, h) * 0.85;
            double radX = radiusPixels / w;
            double radY = radiusPixels / h;

            var windowBgObj = FindResource("BgDeep");
            var windowBg = windowBgObj is SolidColorBrush scbWin ? scbWin.Color : Color.FromRgb(30, 30, 34);
            const double aWin = 0.20;
            var windowTint = Color.FromRgb(
                (byte)(windowBg.R * (1 - aWin) + color.R * aWin),
                (byte)(windowBg.G * (1 - aWin) + color.G * aWin),
                (byte)(windowBg.B * (1 - aWin) + color.B * aWin));

            var winGradient = new RadialGradientBrush
            {
                Center = new Point(relativeX, relativeY),
                GradientOrigin = new Point(relativeX, relativeY),
                RadiusX = radX,
                RadiusY = radY,
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(windowTint, 0.0),
                    new GradientStop(windowBg, 1.0)
                }
            };
            RootBorder.Background = winGradient;
        }
        else
        {
            RootBorder.SetResourceReference(Border.BackgroundProperty, "BgDeep");
        }
    }

    private static Color Tint(Color baseColor, Color tintColor, double ratio)
    {
        return Color.FromArgb(
            baseColor.A,
            (byte)(baseColor.R * (1 - ratio) + tintColor.R * ratio),
            (byte)(baseColor.G * (1 - ratio) + tintColor.G * ratio),
            (byte)(baseColor.B * (1 - ratio) + tintColor.B * ratio));
    }

    private static Color ParseHex(string hex)
    {
        if (ThemeService.TryParseColor(hex, out var c)) return c;
        return Color.FromRgb(0, 0, 0);
    }

    private static string ToHex(Color c)
    {
        if (c.A < 255)
            return $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    internal static ThemeSettings CreateAdaptiveTheme(ThemeSettings baseTheme, Color dominant)
    {
        var adaptive = baseTheme.Clone();
        adaptive.BgDeep = ToHex(Tint(ParseHex(baseTheme.BgDeep), dominant, 0.15));
        adaptive.BgSurface = ToHex(Tint(ParseHex(baseTheme.BgSurface), dominant, 0.18));
        adaptive.BgCard = ToHex(Tint(ParseHex(baseTheme.BgCard), dominant, 0.22));
        adaptive.BgElevated = ToHex(Tint(ParseHex(baseTheme.BgElevated), dominant, 0.26));
        adaptive.BorderSub = ToHex(Tint(ParseHex(baseTheme.BorderSub), dominant, 0.30));
        adaptive.Accent = ToHex(Tint(ParseHex(baseTheme.Accent), dominant, 0.75));
        adaptive.AccentAlt = ToHex(Tint(ParseHex(baseTheme.AccentAlt), dominant, 0.75));
        adaptive.ButtonGrad = ToHex(Tint(ParseHex(baseTheme.ButtonGrad), dominant, 0.65));
        return adaptive;
    }

    private static Color GetDominantColor(BitmapSource bmp)
    {
        var formatted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        int w = formatted.PixelWidth, h = formatted.PixelHeight;
        int stride = w * 4;
        var pixels = new byte[stride * h];
        formatted.CopyPixels(pixels, stride, 0);

        // Sample a grid of pixels and bucket by reduced color
        var buckets = new Dictionary<int, (long r, long g, long b, int count)>();
        int stepX = Math.Max(1, w / 30), stepY = Math.Max(1, h / 30);
        for (int y = 0; y < h; y += stepY)
        {
            int rowOff = y * stride;
            for (int x = 0; x < w; x += stepX)
            {
                int i = rowOff + x * 4;
                byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];

                // Skip very dark or very light pixels (background / highlight)
                int lum = (r * 299 + g * 587 + b * 114) / 1000;
                if (lum < 25 || lum > 240) continue;

                // Reduce to 5-bit per channel for bucketing
                int key = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
                if (buckets.TryGetValue(key, out var v))
                    buckets[key] = (v.r + r, v.g + g, v.b + b, v.count + 1);
                else
                    buckets[key] = (r, g, b, 1);
            }
        }

        if (buckets.Count == 0)
            return Color.FromRgb(30, 30, 34);

        var top = buckets.Values.OrderByDescending(v => v.count).First();
        return Color.FromRgb(
            (byte)(top.r / top.count),
            (byte)(top.g / top.count),
            (byte)(top.b / top.count));
    }

    // ════════════════════════════════════════════════════════════
    //  START JOB
    // ════════════════════════════════════════════════════════════
    private async void Start_Click(object s, RoutedEventArgs e)
    {
        var url = TxtUrl.Text.Trim();
        if (string.IsNullOrEmpty(url)) { Warn(L("ErrNoUrl")); return; }
        if (!_backendReady) { Warn(L("ErrNoBackend")); return; }

        var isUpload = RbUpload.IsChecked == true;
        if (isUpload && string.IsNullOrWhiteSpace(TxtTitle.Text))
        { Warn(L("ErrNoTitle")); return; }
        if (!isUpload && string.IsNullOrWhiteSpace(TxtOutputDir.Text))
        { Warn(L("ErrNoSavePath")); return; }

        var privacy = (CmbPrivacy.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "public";
        var fingerprint = isUpload ? ChkFingerprint.IsChecked == true
                                   : ChkFingerprintDl.IsChecked == true;
        var fingerprintMethod = isUpload
            ? (CmbFingerprintMethod.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "standard"
            : (CmbFingerprintMethodDl.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "standard";
        var title = isUpload ? TxtTitle.Text.Trim() : TxtTitleDl.Text.Trim();
        var quality = (CmbQuality.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "best";

        _isJobRunning = true;
        BtnFetch.IsEnabled = false;
        HideOpenFolderBar();
        _lastDownloadedFolder = null;
        _lastDownloadedFile = null;

        _lastJobLog = string.Empty;
        _lastLogEntry = string.Empty;
        _logViewer?.Close();

        _cts = new CancellationTokenSource();
        _progressWin = new ProgressWindow(_cts, GetBtnRect(StartContainer)) { Owner = this };
        _progressWin.Closed += (_, _) =>
        {
            _lastJobLog = _progressWin?.LogContent ?? string.Empty;
            _progressWin = null;
            var hasLog = !string.IsNullOrEmpty(_lastJobLog);
            BtnMainLog.IsEnabled = hasLog;
            if (!hasLog) BtnMainLog.Visibility = Visibility.Collapsed;
            if (_currentJobId != null)
                _ = _backend.CancelJobAsync(_currentJobId);
            if (!_cts.IsCancellationRequested)
                _cts.Cancel();
        };
        BtnMainLog.IsEnabled = true;
        ShowLogButton();
        
        bool autoShow = SettingsService.Current.AutoShowProgressWindow;
        if (autoShow)
        {
            _progressWin.Show();
        }
        
        _progressWin.UpdateProgress(0, "Starte…", "Verbinde mit Backend…");
        _progressWin.AppendLog("Verbinde mit Backend…");

        UpdateMiniProgress(0, "Verbinde mit Backend…");

        try
        {
            bool isYTMusic = url.ToLowerInvariant().Contains("music.youtube.com") || (_meta?.IsMusic ?? false) || (quality?.StartsWith("ytmusic") ?? false);
            string platformParam = isYTMusic ? "ytmusic" : "auto";

            DevLog($"Creating job: mode={( isUpload ? "upload" : "download" )}, platform={platformParam}, fingerprint={fingerprint} ({fingerprintMethod}), quality={quality}");
            _currentJobId = await _backend.CreateJobAsync(
                url,
                isUpload ? "upload" : "download",
                platformParam,
                title,
                TxtDescription.Text.Trim(),
                _meta?.Tags,
                isUpload ? null : TxtOutputDir.Text.Trim(),
                privacy,
                fingerprint,
                fingerprintMethod,
                SettingsService.Current.UseGpu,
                isUpload ? null : quality,
                _cts.Token);

            _progressWin?.AppendLog($"Job gestartet: {_currentJobId}");

            await foreach (var status in _backend.StreamJobAsync(_currentJobId, _cts.Token))
                {
                    var detail = "";
                    _progressWin?.UpdateProgress(status.Progress, status.Message, detail, status.Eta, status.Speed);
                    UpdateMiniProgress(status.Progress, status.Message);
                    if (!string.IsNullOrEmpty(status.Message))
                    {
                        var entry = $"{status.Progress}% \u2013 {status.Message}";
                        if (entry != _lastLogEntry)
                        {
                            _progressWin?.AppendLog(entry);
                            _lastLogEntry = entry;
                        }
                    }

                    if (status.Status == "completed")
                    {
                        var folderPath = "";
                        var filePath = "";
                        if (status.Result != null)
                        {
                            if (status.Result.TryGetValue("file_path", out var fp))
                                filePath = fp?.ToString() ?? "";
                        }
                        if (!string.IsNullOrEmpty(filePath))
                        {
                            folderPath = System.IO.Path.GetDirectoryName(filePath) ?? "";
                        }
                        else if (!isUpload)
                        {
                            folderPath = TxtOutputDir.Text.Trim();
                        }

                        _progressWin?.MarkDone(true, folderPath);

                        _lastDownloadedFolder = folderPath;
                        _lastDownloadedFile = filePath;
                        if (!string.IsNullOrEmpty(folderPath))
                            ShowOpenFolderBar();

                        break;
                    }

                if (status.Status == "error")
                {
                    _progressWin?.AppendLog($"[Fehler] {status.Error}");
                    SetStatus($"{L("StatusErrPrefix")} {status.Error}", false);
                    _progressWin?.MarkDone(false);
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (_currentJobId != null)
                await _backend.CancelJobAsync(_currentJobId);
            SetStatus(L("StatusCancelled"), false);
        }
        catch (Exception ex)
        {
            SetStatus($"{L("StatusErrPrefix")} {ex.Message}", false);
            _progressWin?.MarkDone(false);
        }
        finally
        {
            _currentJobId = null;
            BtnFetch.IsEnabled = true;
            ResetStartContainerToIdle();
        }
    }

    // ════════════════════════════════════════════════════════════
    //  LANGUAGE
    // ════════════════════════════════════════════════════════════
    private static string L(string key)
        => Application.Current.Resources[key] as string ?? key;

    private DateTime _langPopupClosedTime = DateTime.MinValue;

    private void LangPopup_Closed(object sender, EventArgs e)
    {
        _langPopupClosedTime = DateTime.UtcNow;
        TxtLangArrow.Text = ">>";
    }

    private void LangPill_Click(object s, RoutedEventArgs e)
    {
        if (LangPopup == null) return;
        if ((DateTime.UtcNow - _langPopupClosedTime).TotalMilliseconds < 250)
        {
            return;
        }

        LangPopup.PlacementTarget = BtnLangPill;
        if (!LangPopup.IsOpen)
        {
            UpdateLanguageCheckmarks();
            LangPopup.IsOpen = true;
            TxtLangArrow.Text = "⌄⌄";
            AnimatePopupIn(LangPopupBorder);
        }
        else
        {
            TxtLangArrow.Text = ">>";
            AnimatePopupOut(LangPopupBorder, () => LangPopup.IsOpen = false);
        }
    }

    private void SelectLang_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string lang)
        {
            SetLanguage(lang);
            AnimatePopupOut(LangPopupBorder, () => LangPopup.IsOpen = false);
        }
    }

    private void UpdateLanguageCheckmarks()
    {
        if (CheckLangDE != null) CheckLangDE.Visibility = _currentLang == "de" ? Visibility.Visible : Visibility.Collapsed;
        if (CheckLangEN != null) CheckLangEN.Visibility = _currentLang == "en" ? Visibility.Visible : Visibility.Collapsed;
        if (CheckLangES != null) CheckLangES.Visibility = _currentLang == "es" ? Visibility.Visible : Visibility.Collapsed;
        if (CheckLangFR != null) CheckLangFR.Visibility = _currentLang == "fr" ? Visibility.Visible : Visibility.Collapsed;
        if (CheckLangIT != null) CheckLangIT.Visibility = _currentLang == "it" ? Visibility.Visible : Visibility.Collapsed;
        if (CheckLangJA != null) CheckLangJA.Visibility = _currentLang == "ja" ? Visibility.Visible : Visibility.Collapsed;
        if (CheckLangZH != null) CheckLangZH.Visibility = _currentLang == "zh" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetLanguage(string lang)
    {
        _currentLang = lang;

        if (TxtCurrentLang != null)
            TxtCurrentLang.Text = lang.ToUpperInvariant();

        UpdateLanguageCheckmarks();

        var dicts = Application.Current.Resources.MergedDictionaries;
        
        // Remove all previous language override dictionaries
        var oldLangs = dicts.Where(d => d.Contains("LangCode")).ToList();
        foreach (var old in oldLangs) dicts.Remove(old);

        // Always ensure base English dictionary is loaded as fallback
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

        SettingsService.Current.Language = lang;
        SettingsService.Save(SettingsService.Current);
    }

    private void MiniProgress_Click(object sender, MouseButtonEventArgs e)
    {
        if (_progressWin != null)
        {
            HighlightMiniProgress(false);
            if (_progressWin.Visibility != Visibility.Visible)
            {
                _progressWin.ShowWithAnimation();
            }
            else
            {
                if (_progressWin.WindowState == WindowState.Minimized)
                    _progressWin.WindowState = WindowState.Normal;
                _progressWin.Activate();
            }
            _progressWin.EnsureLogOpen();
        }
    }

    private bool _isJobRunning = false;

    private void StartContainer_Click(object sender, MouseButtonEventArgs e)
    {
        if (_isJobRunning)
        {
            if (_progressWin != null)
            {
                if (_progressWin.Visibility != Visibility.Visible)
                    _progressWin.ShowWithAnimation();
                else
                {
                    if (_progressWin.WindowState == WindowState.Minimized)
                        _progressWin.WindowState = WindowState.Normal;
                    _progressWin.Activate();
                }
                _progressWin.EnsureLogOpen();
            }
            return;
        }

        Start_Click(sender, e);
    }

    private double _currentMiniProgressPercent = 0;

    private static string GetMinimalStatusLabel(string rawMsg, double percentage)
    {
        if (string.IsNullOrWhiteSpace(rawMsg))
            return $"{percentage:0}%";

        string lower = rawMsg.ToLowerInvariant();

        if (lower.Contains("fingerprint"))
        {
            return $"Fingerprint Bypass   {percentage:0}%";
        }
        if (lower.Contains("upload"))
        {
            return $"YouTube Upload   {percentage:0}%";
        }
        if (lower.Contains("export"))
        {
            return $"Export   {percentage:0}%";
        }
        if (lower.Contains("merg") || lower.Contains("zusammenführ") || lower.Contains("zusammenfüg"))
        {
            return $"Merging   {percentage:0}%";
        }
        if (lower.Contains("download audio") || (lower.Contains("audio") && lower.Contains("download")))
        {
            return $"Download Audio   {percentage:0}%";
        }
        if (lower.Contains("download video") || lower.Contains("download") || lower.Contains("lade"))
        {
            return $"Download Video   {percentage:0}%";
        }
        if (lower.Contains("verbinde") || lower.Contains("starte"))
        {
            return $"Starte   {percentage:0}%";
        }

        var title = rawMsg;
        if (title.StartsWith("["))
        {
            var idx = title.IndexOf(']');
            if (idx >= 0 && idx + 1 < title.Length)
                title = title.Substring(idx + 1).Trim();
        }
        if (title.Contains(":"))
        {
            title = title.Split(':')[0].Trim();
        }
        title = title.TrimEnd('.', '…');

        return $"{title}   {percentage:0}%";
    }

    private void UpdateMiniProgress(double percentage, string? statusMsg = null)
    {
        _currentMiniProgressPercent = percentage;

        if (StartContainer != null && StartProgressFill != null)
        {
            if (StartIdleBg != null) StartIdleBg.Visibility = Visibility.Collapsed;
            if (StartTrackGroove != null) StartTrackGroove.Visibility = Visibility.Visible;
            if (StartProgressFill != null) StartProgressFill.Visibility = Visibility.Visible;

            double containerWidth = StartContainer.ActualWidth;
            if (containerWidth <= 0) containerWidth = 300.0;
            double maxFillWidth = Math.Max(0, containerWidth - 6.0);

            double targetWidth = 0;
            if (percentage > 0)
            {
                targetWidth = Math.Clamp(maxFillWidth * (percentage / 100.0), 34.0, maxFillWidth);
            }

            FluidMotion.AnimateProgressWidth(StartProgressFill, targetWidth);

            if (TxtStartLabel != null && !string.IsNullOrWhiteSpace(statusMsg))
            {
                TxtStartLabel.Text = GetMinimalStatusLabel(statusMsg, percentage);
            }
        }
    }

    private void ResetStartContainerToIdle()
    {
        _isJobRunning = false;
        _currentMiniProgressPercent = 0;
        if (StartIdleBg != null) StartIdleBg.Visibility = Visibility.Visible;
        if (StartTrackGroove != null) StartTrackGroove.Visibility = Visibility.Collapsed;
        if (StartProgressFill != null) StartProgressFill.Visibility = Visibility.Collapsed;
        if (StartProgressFill != null) StartProgressFill.Width = 0;
        if (TxtStartLabel != null) TxtStartLabel.SetResourceReference(TextBlock.TextProperty, "BtnStart");
    }

    private void MiniProgressContainer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_currentMiniProgressPercent > 0)
        {
            UpdateMiniProgress(_currentMiniProgressPercent);
        }
    }

    private void HighlightMiniProgress(bool highlight)
    {
        if (highlight)
        {
            // Set border brush to accent color
            var accentBrush = TryFindResource("Accent") as Brush;
            if (accentBrush != null)
                MiniProgressContainer.BorderBrush = accentBrush;

            // Update glow color to dynamic accent
            if (accentBrush is SolidColorBrush scb)
                MiniProgressGlow.Color = scb.Color;

            // Start glow storyboard
            if (MiniProgressContainer.Resources["GlowPulse"] is Storyboard sb)
            {
                sb.Begin(MiniProgressContainer, true);
            }
        }
        else
        {
            // Reset border brush to standard BorderSub
            MiniProgressContainer.BorderBrush = TryFindResource("BorderSub") as Brush ?? new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));

            // Stop glow storyboard
            if (MiniProgressContainer.Resources["GlowPulse"] is Storyboard sb)
            {
                sb.Stop(MiniProgressContainer);
            }

            MiniProgressGlow.BlurRadius = 0;
            MiniProgressGlow.Opacity = 0;
        }
    }

    // ════════════════════════════════════════════════════════════
    //  SETTINGS
    // ════════════════════════════════════════════════════════════
    private void Settings_Click(object s, RoutedEventArgs e)
    {
        foreach (Window w in OwnedWindows)
        {
            if (w is SettingsWindow existing)
            {
                existing.Focus();
                return;
            }
        }

        var win = new SettingsWindow(GetBtnRect((UIElement)s)) { Owner = this };
        win.Closed += (sender, args) =>
        {
            if (win.IsSaved)
                ApplySettings();
        };
        win.Show();
    }

    // ════════════════════════════════════════════════════════════
    //  VIDEO EDITOR
    // ════════════════════════════════════════════════════════════
    private void Editor_Click(object s, RoutedEventArgs e)
    {
        if (Services.SettingsService.Current.EnableDeveloperMode)
        {
            foreach (Window w in OwnedWindows)
            {
                if (w is EditorWindow existing)
                {
                    existing.Focus();
                    return;
                }
            }

            var win = new EditorWindow(GetBtnRect((UIElement)s)) { Owner = this };
            win.Show();
        }
        else
        {
            NotificationWindow.Show(L("ErrEditorDisabled"), this, NotificationType.Warning);
        }
    }

    // ════════════════════════════════════════════════════════════
    //  THEME DESIGNER
    // ════════════════════════════════════════════════════════════
    private void Designer_Click(object s, RoutedEventArgs e)
    {
        foreach (Window w in OwnedWindows)
        {
            if (w is DesignerWindow existing)
            {
                existing.Focus();
                return;
            }
        }

        var win = new DesignerWindow(GetBtnRect((UIElement)s)) { Owner = this };
        win.Closed += (sender, args) =>
        {
            if (win.IsSaved)
            {
                if (_lastDominantColor is Color color)
                {
                    if (ThemeService.Current.AdaptiveThumbnailTheme)
                        ThemeService.Apply(CreateAdaptiveTheme(ThemeService.Current, color));
                    else
                        ThemeService.Apply(ThemeService.Current);
                }
                else
                {
                    ThemeService.Apply(ThemeService.Current);
                }
            }
        };
        win.Show();
    }

    private void ApplySettings()
    {
        var s = SettingsService.Current;
        if (s.Language != _currentLang) SetLanguage(s.Language);
        Topmost = s.AlwaysOnTop;
        SelectComboByTag(CmbPrivacy, s.DefaultPrivacy);
        ChkFingerprint.IsChecked = s.DefaultFingerprintEnabled;
        SelectComboByTag(CmbFingerprintMethod, s.DefaultFingerprintMethod);
        if (!string.IsNullOrEmpty(s.DefaultOutputDir))
            TxtOutputDir.Text = s.DefaultOutputDir;
        ChkFingerprintDl.IsChecked = s.DefaultFingerprintDlEnabled;
        SelectComboByTag(CmbFingerprintMethodDl, s.DefaultFingerprintDlMethod);
        BorderDevConsole.Visibility = s.DevConsoleEnabled && _devConsoleWin == null
            ? Visibility.Visible : Visibility.Collapsed;
        UpdateDevConsoleSpacing();
        if (!s.DevConsoleEnabled) _devConsoleWin?.Close();
        SettingsService.ApplyWindowSize(this);
        SettingsService.ApplyScrollbarVisibility();
    }

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
            if (item.Tag?.ToString() == tag) { combo.SelectedItem = item; return; }
    }

    // ════════════════════════════════════════════════════════════
    //  CLEAR FORM
    // ════════════════════════════════════════════════════════════
    private void ClearForm_Click(object s, RoutedEventArgs e)
    {
        TxtUrl.Text = string.Empty;
        TxtTitleDl.Text = string.Empty;
        _meta = null;
        ImgThumbnail.Source = null;
        TxtMetaThumbFallback.Visibility = Visibility.Visible;
        ResetThumbBackground();
        BorderYTMusicBadge.Visibility = Visibility.Collapsed;
        TxtMetaAlbum.Visibility = Visibility.Collapsed;
        UpdateQualityOptionsVisibility(false);
        AnimatePanel(BorderMeta, false);
    }

    // ════════════════════════════════════════════════════════════
    //  QUICK ACTIONS
    // ════════════════════════════════════════════════════════════
    private void CopyTitle_Click(object s, RoutedEventArgs e)
    {
        if (_meta is not null && !string.IsNullOrEmpty(_meta.Title))
            Clipboard.SetText(_meta.Title);
    }

    private void CopyUrl_Click(object s, RoutedEventArgs e)
    {
        var url = TxtUrl.Text.Trim();
        if (!string.IsNullOrEmpty(url))
            Clipboard.SetText(url);
    }

    private void OpenInBrowser_Click(object s, RoutedEventArgs e)
    {
        var url = TxtUrl.Text.Trim();
        if (!string.IsNullOrEmpty(url))
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void SaveThumbnail_Click(object s, RoutedEventArgs e)
    {
        if (_meta is null || string.IsNullOrWhiteSpace(_meta.Thumbnail)) return;
        var dlg = new SaveFileDialog
        {
            Title = L("SaveThumbnailTitle"),
            Filter = "JPEG|*.jpg|PNG|*.png",
            FileName = "thumbnail.jpg",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(_meta.Thumbnail, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();

            BitmapEncoder encoder = dlg.FilterIndex == 2
                ? new PngBitmapEncoder()
                : new JpegBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));

            using var fs = System.IO.File.Create(dlg.FileName);
            encoder.Save(fs);
        }
        catch (Exception ex)
        {
            Warn($"{L("ErrPrefix")} {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════
    //  ANIMATIONS
    // ════════════════════════════════════════════════════════════
    private static void AnimatePanel(Border panel, bool show)
    {
        if (show)
            FluidMotion.ShowPanel(panel);
        else
            FluidMotion.HidePanel(panel);
    }

    private void ShowLogButton()
    {
        if (BtnMainLog.Visibility == Visibility.Visible) return;

        BtnMainLog.Visibility = Visibility.Visible;
        BtnMainLog.Opacity = 0;
        BtnMainLog.RenderTransformOrigin = new Point(0.5, 0.5);
        var st = new ScaleTransform(0.85, 0.85);
        BtnMainLog.RenderTransform = st;

        var spring = AppleSpringEase.Interactive;
        var smooth = AppleSpringEase.Gentle;

        BtnMainLog.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
            { EasingFunction = smooth });
        st.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(450))
            { EasingFunction = spring });
        st.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(450))
            { EasingFunction = spring });
    }

    private void ShowOpenFolderBar()
    {
        if (BorderCompletedActions.Visibility == Visibility.Visible) return;
        if (RbDownload?.IsChecked != true) return;

        BorderCompletedActions.Visibility = Visibility.Visible;
        BorderCompletedActions.Opacity = 0;
        BorderCompletedActions.RenderTransformOrigin = new Point(0.5, 0.5);
        var cardGroup = new TransformGroup();
        var cardScale = new ScaleTransform(0.93, 0.93);
        var cardTrans = new TranslateTransform(0, 10);
        cardGroup.Children.Add(cardScale);
        cardGroup.Children.Add(cardTrans);
        BorderCompletedActions.RenderTransform = cardGroup;

        var spring = AppleSpringEase.Interactive;
        var smooth = AppleSpringEase.Gentle;

        BorderCompletedActions.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)) { EasingFunction = smooth });
        cardScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.93, 1, TimeSpan.FromMilliseconds(480)) { EasingFunction = spring });
        cardScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.93, 1, TimeSpan.FromMilliseconds(480)) { EasingFunction = spring });
        cardTrans.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(480)) { EasingFunction = spring });

        // Stagger the buttons inside the card
        var buttons = new[] { BtnOpenFileLocation, BtnOpenInEditor };
        var staggerSpring = AppleSpringEase.Bouncy;
        for (int i = 0; i < buttons.Length; i++)
        {
            var btn = buttons[i];
            if (btn == null) continue;

            btn.Opacity = 0;
            btn.RenderTransformOrigin = new Point(0.5, 0.5);
            var btnGroup = new TransformGroup();
            var btnScale = new ScaleTransform(0.95, 0.95);
            var btnTrans = new TranslateTransform(0, 8);
            btnGroup.Children.Add(btnScale);
            btnGroup.Children.Add(btnTrans);
            btn.RenderTransform = btnGroup;

            var delay = TimeSpan.FromMilliseconds(80 + i * 70);

            btn.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
                { BeginTime = delay, EasingFunction = smooth });

            btnScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(400))
                { BeginTime = delay, EasingFunction = staggerSpring });
            btnScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(400))
                { BeginTime = delay, EasingFunction = staggerSpring });

            btnTrans.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(400))
                { BeginTime = delay, EasingFunction = staggerSpring });
        }
    }

    private void HideOpenFolderBar()
    {
        if (BorderCompletedActions.Visibility != Visibility.Visible) return;

        var ease = AppleSpringEase.Snappy;
        var dur = TimeSpan.FromMilliseconds(200);

        BorderCompletedActions.RenderTransformOrigin = new Point(0.5, 0.5);
        var group = new TransformGroup();
        var st = new ScaleTransform(1, 1);
        var tt = new TranslateTransform(0, 0);
        group.Children.Add(st);
        group.Children.Add(tt);
        BorderCompletedActions.RenderTransform = group;

        var opAnim = new DoubleAnimation(1, 0, dur) { EasingFunction = ease };
        opAnim.Completed += (_, _) =>
        {
            BorderCompletedActions.Visibility = Visibility.Collapsed;
        };

        BorderCompletedActions.BeginAnimation(UIElement.OpacityProperty, opAnim);
        tt.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, 6, dur) { EasingFunction = ease });
        st.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, 0.97, dur) { EasingFunction = ease });
        st.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, 0.97, dur) { EasingFunction = ease });
    }

    private void OpenFileLocation_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_lastDownloadedFolder) && System.IO.Directory.Exists(_lastDownloadedFolder))
            System.Diagnostics.Process.Start("explorer.exe", _lastDownloadedFolder);
    }

    private void OpenInEditor_Click(object sender, RoutedEventArgs e)
    {
        if (Services.SettingsService.Current.EnableDeveloperMode)
        {
            if (string.IsNullOrEmpty(_lastDownloadedFile) || !System.IO.File.Exists(_lastDownloadedFile))
                return;

            foreach (Window w in OwnedWindows)
            {
                if (w is EditorWindow existing)
                {
                    existing.Focus();
                    existing.LoadVideo(_lastDownloadedFile);
                    return;
                }
            }

            var win = new EditorWindow(GetBtnRect((UIElement)sender), _lastDownloadedFile) { Owner = this };
            win.Show();
        }
        else
        {
            NotificationWindow.Show(L("ErrEditorDisabled"), this, NotificationType.Warning);
        }
    }

    private static void AnimatePopupIn(Border border)
    {
        var spring = AppleSpringEase.Interactive;
        var bouncy = AppleSpringEase.Bouncy;
        var smooth = AppleSpringEase.Gentle;
        var group = (TransformGroup)border.RenderTransform;
        var st = (ScaleTransform)group.Children[0];
        var tt = (TranslateTransform)group.Children[1];

        // Reset corner radius to bubbly state
        FluidMotion.SetCornerRadiusImmediate(border, 55);

        border.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            { EasingFunction = smooth });
        st.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(420))
            { EasingFunction = spring });
        st.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(420))
            { EasingFunction = spring });
        tt.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(-4, 0, TimeSpan.FromMilliseconds(420))
            { EasingFunction = spring });

        // Morph corner radius to normal rounded corner (12) using a spring
        FluidMotion.AnimateCornerRadius(border, 12, TimeSpan.FromMilliseconds(550), bouncy);
    }

    private static void AnimatePopupOut(Border border, Action onDone)
    {
        var ease = AppleSpringEase.Snappy;
        var group = (TransformGroup)border.RenderTransform;
        var st = (ScaleTransform)group.Children[0];
        var tt = (TranslateTransform)group.Children[1];

        // Morph corner radius back to bubbly state (55) quickly
        FluidMotion.AnimateCornerRadius(border, 55, TimeSpan.FromMilliseconds(160), ease);

        var opAnim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(160))
        { EasingFunction = ease };
        opAnim.Completed += (_, _) => onDone();

        border.BeginAnimation(UIElement.OpacityProperty, opAnim);
        st.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, 0.96, TimeSpan.FromMilliseconds(160))
            { EasingFunction = ease });
        st.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, 0.96, TimeSpan.FromMilliseconds(160))
            { EasingFunction = ease });
        tt.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, -4, TimeSpan.FromMilliseconds(160))
            { EasingFunction = ease });
    }

    // ════════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════════
    private void DevLog(string message)
    {
        var category = "system";
        var lower = message.ToLowerInvariant();
        if (lower.Contains("[backend]") || lower.Contains("[ping]") || lower.Contains("[status]") || lower.Contains("[restart]") || lower.Contains("pinging backend") || lower.Contains("restarting backend"))
        {
            category = "backend";
        }
        else if (lower.Contains("[ffmpeg]") || lower.Contains("[yt-dlp]") || lower.Contains("ffmpeg") || lower.Contains("yt-dlp"))
        {
            category = "ffmpeg";
        }

        var s = SettingsService.Current;
        if (category == "system" && !s.ConsoleShowSystem) return;
        if (category == "backend" && !s.ConsoleShowBackend) return;
        if (category == "ffmpeg" && !s.ConsoleShowFFmpeg) return;

        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        
        // Always record in the main TextBox buffer so history is never lost
        TxtDevConsole.AppendText(TxtDevConsole.Text.Length == 0 ? line : Environment.NewLine + line);
        TxtDevConsole.ScrollToEnd();
        
        // Also append to the detached console window if it is currently open
        _devConsoleWin?.AppendLog(line);
    }

    private void ClearDevConsole_Click(object s, RoutedEventArgs e)
        => TxtDevConsole.Clear();

    private void DevConsole_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            DetachDevConsole();
            e.Handled = true;
        }
    }

    private void DetachDevConsole_Click(object sender, RoutedEventArgs e)
    {
        DetachDevConsole();
    }

    private void DetachDevConsole()
    {
        if (_devConsoleWin != null)
        {
            _devConsoleWin.Focus();
            return;
        }

        Rect originRect = GetBtnRect(BorderDevConsole);
        _devConsoleWin = new DevConsoleWindow(TxtDevConsole.Text, originRect)
        {
            Owner = this,
            Title = "Console"
        };

        _devConsoleWin.CommandEntered += (cmd) =>
        {
            HandleConsoleCommand(cmd);
        };

        _devConsoleWin.Closed += (_, _) =>
        {
            if (_devConsoleWin != null && _devConsoleWin.ReattachRequested)
            {
                BorderDevConsole.Visibility = Visibility.Visible;
                UpdateDevConsoleSpacing();
            }
            _devConsoleWin = null;
        };

        BorderDevConsole.Visibility = Visibility.Collapsed;
        UpdateDevConsoleSpacing();

        _devConsoleWin.Show();
    }

    private void CollapseDevConsole_Click(object s, RoutedEventArgs e)
    {
        _devConsoleCollapsed = !_devConsoleCollapsed;
        TxtCollapseIcon.Text = _devConsoleCollapsed ? "▸" : "▾";

        if (_devConsoleCollapsed)
            FluidMotion.HideBody(DevConsoleBody, DevConsoleContentRow, () => { });
        else
            FluidMotion.ShowBody(DevConsoleBody, DevConsoleContentRow, 120);
    }

    private void TxtDevConsoleInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var cmd = TxtDevConsoleInput.Text.Trim();
            if (string.IsNullOrEmpty(cmd)) return;
            _inlineCmdHistory.Add(cmd);
            _inlineCmdHistoryIndex = _inlineCmdHistory.Count;
            DevLog($"> {cmd}");
            HandleConsoleCommand(cmd);
            TxtDevConsoleInput.Clear();
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (_inlineCmdHistory.Count > 0 && _inlineCmdHistoryIndex > 0)
            {
                _inlineCmdHistoryIndex--;
                TxtDevConsoleInput.Text = _inlineCmdHistory[_inlineCmdHistoryIndex];
                TxtDevConsoleInput.CaretIndex = TxtDevConsoleInput.Text.Length;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            if (_inlineCmdHistoryIndex < _inlineCmdHistory.Count - 1)
            {
                _inlineCmdHistoryIndex++;
                TxtDevConsoleInput.Text = _inlineCmdHistory[_inlineCmdHistoryIndex];
                TxtDevConsoleInput.CaretIndex = TxtDevConsoleInput.Text.Length;
            }
            else
            {
                _inlineCmdHistoryIndex = _inlineCmdHistory.Count;
                TxtDevConsoleInput.Clear();
            }
            e.Handled = true;
        }
    }

    private void HandleConsoleCommand(string cmd)
    {
        var trimCmd = cmd.Trim();
        var lower = trimCmd.ToLowerInvariant();
        
        if (lower == "help")
        {
            DevLog("Available commands:");
            DevLog("  help                 - Show this help list");
            DevLog("  clear / cls          - Clear the console window");
            DevLog("  status               - Show backend connection status");
            DevLog("  ping                 - Ping backend and measure latency");
            DevLog("  restart              - Restart the Python backend service");
            DevLog("  config               - Display current settings config");
            DevLog("  python               - Check Python installation path & version");
            DevLog("  ffmpeg               - Check FFmpeg installation path & version");
            DevLog("  open [downloads|app] - Open downloads or application data folder");
            DevLog("  resize [h]/[w] [h]   - Resize inline console height or detached window size");
            DevLog("  info                 - Show application version information");
            DevLog("  Any other input is sent directly to the backend process stdin.");
            return;
        }

        if (lower.StartsWith("resize"))
        {
            var parts = trimCmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && double.TryParse(parts[1], out double h))
            {
                if (h >= 50 && h <= 1000)
                {
                    DevConsoleContentRow.Height = new GridLength(h);
                    if (_devConsoleWin != null)
                    {
                        _devConsoleWin.Height = h;
                    }
                    DevLog($"[resize] Console height set to {h}px.");
                }
                else
                {
                    DevLog("[resize] Error: Height must be between 50 and 1000.");
                }
            }
            else if (parts.Length == 3 && double.TryParse(parts[1], out double w) && double.TryParse(parts[2], out h))
            {
                if (w >= 200 && w <= 2000 && h >= 100 && h <= 1500)
                {
                    if (_devConsoleWin != null)
                    {
                        _devConsoleWin.Width = w;
                        _devConsoleWin.Height = h;
                        DevLog($"[resize] Detached console window resized to {w}x{h}px.");
                    }
                    else
                    {
                        DevConsoleContentRow.Height = new GridLength(h);
                        DevLog($"[resize] Inline console height set to {h}px (detached window not open).");
                    }
                }
                else
                {
                    DevLog("[resize] Error: Width must be 200-2000, height 100-1500.");
                }
            }
            else
            {
                DevLog("Usage: resize <height>  OR  resize <width> <height>");
            }
            return;
        }

        switch (lower)
        {
            case "clear":
            case "cls":
                TxtDevConsole.Clear();
                _devConsoleWin?.ClearConsole();
                break;
            case "status":
                DevLog($"[status] Backend Ready: {_backendReady}");
                DevLog($"[status] Backend URL: {SettingsService.Current.BackendUrl}");
                DevLog($"[status] Developer Mode: {(SettingsService.Current.EnableDeveloperMode ? "ENABLED" : "DISABLED")}");
                break;
            case "dev":
            case "devmode":
                DevLog($"[devmode] Developer Mode is {(SettingsService.Current.EnableDeveloperMode ? "ACTIVE (Video Editor & Dev Tools Unlocked)" : "INACTIVE (Video Editor Disabled)")}");
                break;
            case "editor":
                if (SettingsService.Current.EnableDeveloperMode)
                {
                    DevLog("[editor] Launching Video Editor...");
                    Editor_Click(this, new RoutedEventArgs());
                }
                else
                {
                    DevLog("[editor] Video Editor is blocked. Enable Developer Mode in Settings to unlock.");
                    NotificationWindow.Show(L("ErrEditorDisabled"), this, NotificationType.Warning);
                }
                break;
            case "ping":
                DevLog("Pinging backend server...");
                System.Threading.Tasks.Task.Run(async () =>
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    bool ok = await _backend.WaitForHealthAsync(System.Threading.CancellationToken.None, 1);
                    sw.Stop();
                    Dispatcher.Invoke(() => {
                        if (ok) DevLog($"[ping] Pong! Backend responded in {sw.ElapsedMilliseconds} ms.");
                        else DevLog("[ping] Ping failed. Backend is not responding.");
                    });
                });
                break;
            case "restart":
                DevLog("Restarting backend from console...");
                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        _launcher.Dispose();
                        _launcher.Start();
                        _backendReady = await _backend.WaitForHealthAsync(
                            System.Threading.CancellationToken.None, SettingsService.Current.BackendTimeoutSeconds);
                        Dispatcher.Invoke(() => {
                            SetStatus(_backendReady ? L("StatusBackendReady") : L("StatusBackendDown"), _backendReady);
                            DevLog(_backendReady ? "[restart] Backend restarted successfully." : "[restart] Backend restart failed.");
                        });
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => {
                            SetStatus($"{L("StatusBackendErrPrefix")} {ex.Message}", false);
                            DevLog($"[restart] Error: {ex.Message}");
                        });
                    }
                });
                break;
            case "config":
                DevLog("Current Configuration Settings:");
                DevLog($"  Backend URL:          {SettingsService.Current.BackendUrl}");
                DevLog($"  Startup Timeout:      {SettingsService.Current.BackendTimeoutSeconds}s");
                DevLog($"  Auto-Paste URL:       {SettingsService.Current.AutoPasteOnFocus}");
                DevLog($"  Default Privacy:      {SettingsService.Current.DefaultPrivacy}");
                DevLog($"  Auto #Shorts Tag:     {SettingsService.Current.AutoAddShortsHashtag}");
                DevLog($"  Bypass Fingerprint:   {SettingsService.Current.DefaultFingerprintEnabled}");
                DevLog($"  Default Save Path:    {SettingsService.Current.DefaultOutputDir}");
                DevLog($"  GPU Acceleration:     {SettingsService.Current.UseGpu}");
                DevLog($"  Always On Top:        {SettingsService.Current.AlwaysOnTop}");
                DevLog($"  Notify On Complete:   {SettingsService.Current.NotifyOnComplete}");
                DevLog($"  Max Concurrent Jobs:  {SettingsService.Current.MaxConcurrentJobs}");
                break;
            case "python":
                DevLog("Checking system Python...");
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        var pInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "python",
                            Arguments = "--version",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };
                        using var p = System.Diagnostics.Process.Start(pInfo);
                        if (p != null)
                        {
                            p.WaitForExit(3000);
                            var output = (p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd()).Trim();
                            Dispatcher.Invoke(() => DevLog($"[python] Version: {output}"));
                        }
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => DevLog($"[python] Failed to call 'python --version' on PATH: {ex.Message}"));
                    }
                });
                break;
            case "ffmpeg":
                DevLog("Checking system FFmpeg...");
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        var pInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = FindFfmpegExe(),
                            Arguments = "-version",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };
                        using var p = System.Diagnostics.Process.Start(pInfo);
                        if (p != null)
                        {
                            p.WaitForExit(3000);
                            var line = p.StandardOutput.ReadLine();
                            Dispatcher.Invoke(() => DevLog($"[ffmpeg] Version: {(line ?? "Unknown").Trim()}"));
                        }
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => DevLog($"[ffmpeg] Failed to call 'ffmpeg -version' on PATH: {ex.Message}"));
                    }
                });
                break;
            case "info":
                DevLog("ReelsConverter developer console");
                DevLog($"App version: 26H2 Beta");
                DevLog($"Language: {SettingsService.Current.Language}");
                break;
            case "open":
            case "open downloads":
            case "open appdata":
            case "open app":
                if (lower == "open")
                {
                    DevLog("Open command targets: downloads, appdata");
                }
                else if (lower == "open downloads")
                {
                    var path = SettingsService.Current.DefaultOutputDir;
                    if (string.IsNullOrEmpty(path))
                        path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                    DevLog($"Opening downloads folder: {path}");
                    if (System.IO.Directory.Exists(path))
                        System.Diagnostics.Process.Start("explorer.exe", path);
                    else
                        DevLog("[open] Directory does not exist.");
                }
                else
                {
                    var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReelsConverter");
                    DevLog($"Opening app data folder: {path}");
                    if (System.IO.Directory.Exists(path))
                        System.Diagnostics.Process.Start("explorer.exe", path);
                    else
                        DevLog("[open] Directory does not exist.");
                }
                break;
            default:
                _launcher.SendInput(trimCmd);
                DevLog($"[sent to backend] {trimCmd}");
                break;
        }
    }

    private void DevConsoleHelp_Click(object s, RoutedEventArgs e)
    {
        DevConsoleHelpPopup.PlacementTarget = (UIElement)s;
        if (!DevConsoleHelpPopup.IsOpen)
        {
            DevConsoleHelpPopup.IsOpen = true;
            AnimatePopupIn(DevHelpPopupBorder);
        }
        else
        {
            AnimatePopupOut(DevHelpPopupBorder, () => DevConsoleHelpPopup.IsOpen = false);
        }
    }

    private void UpdateDevConsoleSpacing()
    {
        if (MainContentGrid == null) return;
        bool isConsoleVisible = BorderDevConsole.Visibility == Visibility.Visible;
        double bottomMargin = isConsoleVisible ? 7 : 14;
        MainContentGrid.Margin = new Thickness(14, 6, 14, bottomMargin);
    }

    private void SetStatus(string text, bool ok)
    {
        DevLog($"Status change: {text} (ok={ok})");
        if (ok)
        {
            BtnErrorStatus.Visibility = Visibility.Collapsed;
            ErrorPillDivider.Visibility = Visibility.Collapsed;
            if (ErrorPopup.IsOpen) ErrorPopup.IsOpen = false;
        }
        else
        {
            TxtPopupErrorDetails.Text = text;
            TxtPopupErrorFix.Text = GetSuggestedFix(text);
            BtnErrorStatus.Visibility = Visibility.Visible;
            ErrorPillDivider.Visibility = Visibility.Visible;

            // Show/hide Restart Backend button depending on error type
            var lower = text.ToLowerInvariant();
            bool isBackendError = lower.Contains("backend") || lower.Contains("connect") || lower.Contains("port") || lower.Contains("reach");
            BtnRestartBackend.Visibility = isBackendError ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void ErrorStatus_Click(object s, RoutedEventArgs e)
    {
        ErrorPopup.PlacementTarget = (UIElement)s;
        if (!ErrorPopup.IsOpen)
        {
            ErrorPopup.IsOpen = true;
            AnimateErrorPopupIn(ErrorPopupBorder);
        }
        else
        {
            AnimateErrorPopupOut(ErrorPopupBorder, () => ErrorPopup.IsOpen = false);
        }
    }

    private void AnimateErrorPopupIn(Border border)
    {
        var spring = AppleSpringEase.Interactive;
        var bouncy = AppleSpringEase.Bouncy;
        var smooth = AppleSpringEase.Gentle;
        var group = (TransformGroup)border.RenderTransform;
        var st = (ScaleTransform)group.Children[0];
        var tt = (TranslateTransform)group.Children[1];

        // Reset corner radius to bubbly state
        FluidMotion.SetCornerRadiusImmediate(border, 55);

        // Animate Opacity
        border.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240))
            { EasingFunction = smooth });

        // Animate Scale
        st.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(480))
            { EasingFunction = spring });
        st.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(480))
            { EasingFunction = spring });

        // Animate Y position (slide down slightly)
        tt.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(480))
            { EasingFunction = spring });

        // Morph corner radius to normal rounded rectangle (14) using a bouncy spring!
        FluidMotion.AnimateCornerRadius(border, 14, TimeSpan.FromMilliseconds(620), bouncy);

        // Stagger inner elements
        if (border.Child is StackPanel sp)
        {
            FluidMotion.StaggerIn(sp, baseDelayMs: 40, stepMs: 35);
        }
    }

    private void AnimateErrorPopupOut(Border border, Action onDone)
    {
        var ease = AppleSpringEase.Snappy;
        var group = (TransformGroup)border.RenderTransform;
        var st = (ScaleTransform)group.Children[0];
        var tt = (TranslateTransform)group.Children[1];

        // Animate corner radius back to bubbly state (55) quickly!
        FluidMotion.AnimateCornerRadius(border, 55, TimeSpan.FromMilliseconds(200), ease);

        var opAnim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200))
        { EasingFunction = ease };
        opAnim.Completed += (_, _) => onDone();

        border.BeginAnimation(UIElement.OpacityProperty, opAnim);
        st.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, 0.92, TimeSpan.FromMilliseconds(200))
            { EasingFunction = ease });
        st.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, 0.92, TimeSpan.FromMilliseconds(200))
            { EasingFunction = ease });
        tt.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, -10, TimeSpan.FromMilliseconds(200))
            { EasingFunction = ease });
    }

    private void CopyErrorDetails_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var textToCopy = $"Error Details:\n{TxtPopupErrorDetails.Text}\n\nSuggested Fix:\n{TxtPopupErrorFix.Text}";
            Clipboard.SetText(textToCopy);
            NotificationWindow.Show(L("ErrPopupCopySuccess"), this, NotificationType.Info);
        }
        catch (Exception ex)
        {
            DevLog($"Failed to copy error details: {ex.Message}");
        }
    }

    private void ShowLogs_Click(object sender, RoutedEventArgs e)
    {
        AnimateErrorPopupOut(ErrorPopupBorder, () => ErrorPopup.IsOpen = false);
        DetachDevConsole_Click(sender, e);
    }

    private void RestartBackend_Click(object sender, RoutedEventArgs e)
    {
        AnimateErrorPopupOut(ErrorPopupBorder, () => ErrorPopup.IsOpen = false);
        NotificationWindow.Show(L("ErrPopupRestarting"), this, NotificationType.Info);

        System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                DevLog("Restarting backend via launcher...");
                _launcher.Dispose();
                _launcher.Start();
                DevLog("Backend launcher restarted, waiting for health check...");
                _backendReady = await _backend.WaitForHealthAsync(
                    System.Threading.CancellationToken.None, SettingsService.Current.BackendTimeoutSeconds);
                DevLog(_backendReady ? "Backend health check: OK" : "Backend health check: FAILED");
                Dispatcher.Invoke(() => SetStatus(_backendReady ? L("StatusBackendReady") : L("StatusBackendDown"), _backendReady));
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => SetStatus($"{L("StatusBackendErrPrefix")} {ex.Message}", false));
            }
        });
    }

    private string GetSuggestedFix(string error)
    {
        if (string.IsNullOrEmpty(error))
            return _currentLang == "en" ? "No details available." : "Keine Details vorhanden.";

        var lower = error.ToLowerInvariant();
        if (_currentLang == "en")
        {
            if (lower.Contains("backend") || lower.Contains("connect") || lower.Contains("port"))
                return "Ensure the backend service is running and not blocked by a firewall. Try restarting the app.";
            if (lower.Contains("url") || lower.Contains("link"))
                return "Please enter a valid YouTube or video link in the input field.";
            if (lower.Contains("title") || lower.Contains("titel"))
                return "Please enter a title for the video upload.";
            if (lower.Contains("python") || lower.Contains("ffmpeg") || lower.Contains("path"))
                return "Ensure Python 3.12 and FFmpeg are correctly installed and added to the system PATH.";
            if (lower.Contains("output") || lower.Contains("save") || lower.Contains("directory"))
                return "Please select a valid output folder for the download.";
            return "Check the Developer Console (⌘) or local logs for more diagnostics.";
        }
        else
        {
            if (lower.Contains("backend") || lower.Contains("verbindung") || lower.Contains("connect") || lower.Contains("port"))
                return "Stellen Sie sicher, dass das Backend läuft und nicht blockiert wird. Starten Sie die Anwendung neu.";
            if (lower.Contains("url") || lower.Contains("link"))
                return "Bitte geben Sie einen gültigen YouTube- oder Video-Link im URL-Feld ein.";
            if (lower.Contains("titel") || lower.Contains("title"))
                return "Bitte geben Sie einen Titel für das Video ein.";
            if (lower.Contains("python") || lower.Contains("ffmpeg") || lower.Contains("pfad") || lower.Contains("path"))
                return "Stellen Sie sicher, dass Python 3.12 und FFmpeg installiert und im System-PATH eingetragen sind.";
            if (lower.Contains("speicherpfad") || lower.Contains("output") || lower.Contains("ordner") || lower.Contains("save"))
                return "Bitte wählen Sie einen gültigen Ausgabeordner für den Download aus.";
            return "Prüfen Sie die Entwicklerkonsole (⌘) oder die Log-Dateien für weitere Details.";
        }
    }

    private void Warn(string msg)
        => NotificationWindow.Show(msg, this, NotificationType.Warning);

    private void MainLog_Click(object s, RoutedEventArgs e)
    {
        if (_progressWin != null)
        {
            _progressWin.ToggleLog();
            return;
        }

        if (string.IsNullOrEmpty(_lastJobLog)) return;

        if (_logViewer != null)
        {
            _logViewer.Activate();
            return;
        }

        _logViewer = new LogViewerWindow(_lastJobLog, GetBtnRect(BtnMainLog)) { Owner = this };
        _logViewer.Closed += (_, _) => _logViewer = null;
        _logViewer.Show();
    }

    private static Rect GetBtnRect(UIElement el)
    {
        var pos = el.PointToScreen(new Point(0, 0));
        var source = PresentationSource.FromVisual(el);
        if (source?.CompositionTarget != null)
            pos = source.CompositionTarget.TransformFromDevice.Transform(pos);
        var sz = el.RenderSize;
        return new Rect(pos.X, pos.Y, sz.Width, sz.Height);
    }

    // ════════════════════════════════════════════════════════════
    //  DRAG AND DROP
    // ════════════════════════════════════════════════════════════
    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        if (_isLoadingFromDrag) return;

        if (IsLinkDrag(e))
        {
            e.Effects = DragDropEffects.Copy;
            ShowDragOverlay();
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (IsLinkDrag(e))
        {
            e.Effects = DragDropEffects.Copy;
            if (!_isLoadingFromDrag && GridDragDropOverlay.Visibility != Visibility.Visible)
            {
                ShowDragOverlay();
            }
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        if (_isLoadingFromDrag) return;

        // Get mouse position relative to the Window
        var pos = e.GetPosition(this);
        
        // If the mouse is still inside the window bounds, ignore.
        // WPF fires DragLeave when crossing elements in hierarchy, which causes flickering.
        if (pos.X >= 0 && pos.X <= ActualWidth && pos.Y >= 0 && pos.Y <= ActualHeight)
        {
            return;
        }

        HideDragOverlay();
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (IsLinkDrag(e))
        {
            var url = GetDroppedLink(e);
            if (!string.IsNullOrEmpty(url))
            {
                TxtUrl.Text = url;
                _isLoadingFromDrag = true;
                
                ShowLoadingState();
                
                // Yield thread control back to the WPF dispatcher for 100ms
                // to guarantee the UI renders the loading state & spinner.
                await System.Threading.Tasks.Task.Delay(100);
                
                await FetchMetadataAsync(url);
                
                _isLoadingFromDrag = false;
                HideDragOverlay();
            }
            else
            {
                HideDragOverlay();
            }
        }
        else
        {
            HideDragOverlay();
        }
        e.Handled = true;
    }

    private bool IsLinkDrag(DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.Text) || 
            e.Data.GetDataPresent(DataFormats.UnicodeText))
        {
            var text = GetDragText(e);
            if (!string.IsNullOrEmpty(text))
            {
                var lower = text.Trim().ToLowerInvariant();
                return lower.StartsWith("http://") || 
                       lower.StartsWith("https://") || 
                       lower.Contains("youtube.com") || 
                       lower.Contains("youtu.be") || 
                       lower.Contains("instagram.com") || 
                       lower.Contains("tiktok.com");
            }
        }
        return false;
    }

    private string GetDragText(DragEventArgs e)
    {
        try
        {
            if (e.Data.GetDataPresent(DataFormats.UnicodeText))
                return e.Data.GetData(DataFormats.UnicodeText) as string ?? "";
            if (e.Data.GetDataPresent(DataFormats.Text))
                return e.Data.GetData(DataFormats.Text) as string ?? "";
        }
        catch { }
        return "";
    }

    private string GetDroppedLink(DragEventArgs e)
    {
        return GetDragText(e).Trim();
    }

    private void ShowDragOverlay()
    {
        if (GridDragDropOverlay.Visibility == Visibility.Visible) return;

        GridDragDropOverlay.Visibility = Visibility.Visible;
        GridDragDropOverlay.Opacity = 0;

        var scale = (ScaleTransform)((TransformGroup)DragDropOverlayContent.RenderTransform).Children[0];
        scale.ScaleX = 0.9;
        scale.ScaleY = 0.9;

        var spring = AppleSpringEase.Interactive;
        var smooth = AppleSpringEase.Gentle;

        GridDragDropOverlay.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
            { EasingFunction = smooth });

        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(450))
            { EasingFunction = spring });

        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(450))
            { EasingFunction = spring });
    }

    private void HideDragOverlay()
    {
        if (GridDragDropOverlay.Visibility != Visibility.Visible) return;

        var ease = AppleSpringEase.Snappy;
        var dur = TimeSpan.FromMilliseconds(200);

        var scale = (ScaleTransform)((TransformGroup)DragDropOverlayContent.RenderTransform).Children[0];

        var opAnim = new DoubleAnimation(1, 0, dur) { EasingFunction = ease };
        opAnim.Completed += (s, e) => {
            GridDragDropOverlay.Visibility = Visibility.Collapsed;
            ResetOverlayUI();
        };

        GridDragDropOverlay.BeginAnimation(OpacityProperty, opAnim);

        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, 0.93, dur) { EasingFunction = ease });

        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, 0.93, dur) { EasingFunction = ease });
    }

    private void ShowLoadingState()
    {
        TxtDragPrompt.Text = L("StatusLoading");
        StartSpinnerAnimation();
    }

    private void ResetOverlayUI()
    {
        TxtDragPrompt.Text = L("DragDropOverlayText");
        StopSpinnerAnimation();
    }

    private void StartSpinnerAnimation()
    {
        TxtDragIcon.Visibility = Visibility.Collapsed;
        TxtDragSpinner.Visibility = Visibility.Visible;
        DragProgress.Visibility = Visibility.Visible;
        DragDropDashedBorder.Visibility = Visibility.Collapsed;
        
        var spinnerAnimation = new DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = TimeSpan.FromSeconds(1.8),
            RepeatBehavior = RepeatBehavior.Forever
        };
        SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, spinnerAnimation);
    }

    private void StopSpinnerAnimation()
    {
        SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        TxtDragSpinner.Visibility = Visibility.Collapsed;
        TxtDragIcon.Visibility = Visibility.Visible;
        DragProgress.Visibility = Visibility.Collapsed;
        DragDropDashedBorder.Visibility = Visibility.Visible;
    }

    private static string FindFfmpegExe()
    {
        var exeDir = System.IO.Path.GetDirectoryName(Environment.ProcessPath);
        if (exeDir is not null)
        {
            var localPath = System.IO.Path.Combine(exeDir, "backend", "ffmpeg_bin", "ffmpeg.exe");
            if (System.IO.File.Exists(localPath)) return localPath;
            
            var localPath2 = System.IO.Path.Combine(exeDir, "ffmpeg_bin", "ffmpeg.exe");
            if (System.IO.File.Exists(localPath2)) return localPath2;
        }

        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            var path1 = System.IO.Path.Combine(dir, "backend", "ffmpeg_bin", "ffmpeg.exe");
            if (System.IO.File.Exists(path1)) return path1;

            var path2 = System.IO.Path.Combine(dir, "ffmpeg_bin", "ffmpeg.exe");
            if (System.IO.File.Exists(path2)) return path2;

            var parent = System.IO.Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }

        return "ffmpeg";
    }

    public async Task RunFfmpegExportJobAsync(string output, string args, string? srtPath, double trimDurationSecs)
    {
        _isJobRunning = true;
        BtnFetch.IsEnabled = false;
        HideOpenFolderBar();
        _lastDownloadedFolder = null;
        _lastDownloadedFile = null;

        _lastJobLog = string.Empty;
        _lastLogEntry = string.Empty;
        _logViewer?.Close();

        _cts = new CancellationTokenSource();
        _progressWin = new ProgressWindow(_cts, GetBtnRect(StartContainer)) { Owner = this };
        
        _progressWin.Closed += (_, _) =>
        {
            _lastJobLog = _progressWin?.LogContent ?? string.Empty;
            _progressWin = null;
            var hasLog = !string.IsNullOrEmpty(_lastJobLog);
            BtnMainLog.IsEnabled = hasLog;
            if (!hasLog) BtnMainLog.Visibility = Visibility.Collapsed;
            if (!_cts.IsCancellationRequested)
                _cts.Cancel();
        };

        BtnMainLog.IsEnabled = true;
        ShowLogButton();

        bool autoShow = SettingsService.Current.AutoShowProgressWindow;
        if (autoShow)
        {
            _progressWin.Show();
        }

        _progressWin.UpdateProgress(0, "Exportieren…", "Starte ffmpeg…");
        _progressWin.AppendLog("Exportiere Video mit ffmpeg…");

        UpdateMiniProgress(0, "Exportieren…");

        try
        {
            await Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = FindFfmpegExe(),
                    Arguments = args,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                };
                
                using var proc = Process.Start(psi);
                if (proc == null) throw new Exception("ffmpeg konnte nicht gestartet werden.");

                using (var registration = _cts.Token.Register(() => { try { proc.Kill(); } catch {} }))
                {
                    string? line;
                    while ((line = proc.StandardError.ReadLine()) != null)
                    {
                        var logLine = line;
                        Dispatcher.Invoke(() =>
                        {
                            if (_progressWin != null)
                            {
                                _progressWin.AppendLog(logLine);
                                
                                var match = System.Text.RegularExpressions.Regex.Match(logLine, @"time=(\d+):(\d+):(\d+)\.(\d+)");
                                if (match.Success)
                                {
                                    int hrs = int.Parse(match.Groups[1].Value);
                                    int mins = int.Parse(match.Groups[2].Value);
                                    int secs = int.Parse(match.Groups[3].Value);
                                    double currentSecs = hrs * 3600 + mins * 60 + secs;
                                    
                                    int progress = 0;
                                    if (trimDurationSecs > 0)
                                    {
                                        progress = (int)Math.Clamp((currentSecs / trimDurationSecs) * 100, 0, 99);
                                    }
                                    
                                    _progressWin.UpdateProgress(progress, "Exportieren…", $"{progress}% abgeschlossen");
                                    UpdateMiniProgress(progress, "Exportieren…");
                                }
                            }
                        });
                    }

                    proc.WaitForExit();
                    if (proc.ExitCode != 0)
                    {
                        throw new Exception($"ffmpeg beendet mit Code {proc.ExitCode}");
                    }
                }
            });

            _progressWin?.AppendLog("[OK] Export erfolgreich abgeschlossen.");
            SetStatus("Export erfolgreich abgeschlossen", true);
            _progressWin?.MarkDone(true, System.IO.Path.GetDirectoryName(output));
        }
        catch (OperationCanceledException)
        {
            SetStatus("Export abgebrochen", false);
            _progressWin?.MarkDone(false);
        }
        catch (Exception ex)
        {
            SetStatus($"Export Fehler: {ex.Message}", false);
            _progressWin?.MarkDone(false);
        }
        finally
        {
            if (srtPath != null && System.IO.File.Exists(srtPath))
                try { System.IO.File.Delete(srtPath); } catch { }

            BtnFetch.IsEnabled = true;
            ResetStartContainerToIdle();
        }
    }
}
