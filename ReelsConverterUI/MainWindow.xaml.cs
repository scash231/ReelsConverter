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
    private readonly List<string> _inlineCmdHistory = new();
    private int _inlineCmdHistoryIndex = -1;

    // ── Segment pill drag state (iOS 26 interactive tracking) ──
    private bool _pillDragPending;
    private bool _pillDragging;
    private bool _pillSnapFromDrag;
    private double _pillDragAnchor;
    private double _pillLastMoveX;
    private DateTime _pillLastMoveTime;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += (_, _) => { _cts?.Cancel(); _devConsoleWin?.Close(); _launcher.Dispose(); _backend.Dispose(); };
    }

    // ════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ════════════════════════════════════════════════════════════
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplySettings();
        FluidMotion.SetCornerRadiusValue(SegmentIndicator, 50);
        Mode_Changed(sender, e);

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

    private void TxtUrl_TextChanged(object sender, TextChangedEventArgs e)
    {
        var url = TxtUrl.Text.Trim().ToLowerInvariant();
        string? platform = null;
        if (url.Contains("instagram.com") || url.Contains("instagr.am"))
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
        var editor = new DescriptionEditorWindow(TxtDescription.Text, GetBtnRect((UIElement)s)) { Owner = this };
        if (editor.ShowDialog() == true)
            TxtDescription.Text = editor.Description;
    }

    // ════════════════════════════════════════════════════════════
    //  FETCH METADATA
    // ════════════════════════════════════════════════════════════
    private async void Fetch_Click(object s, RoutedEventArgs e)
    {
        var url = TxtUrl.Text.Trim();
        if (string.IsNullOrEmpty(url)) { Warn(L("ErrNoUrl")); return; }
        if (!_backendReady) { Warn(L("ErrNoBackend")); return; }

        BtnFetch.IsEnabled = false;
        SetStatus(L("StatusLoading"), true);

        try
        {
            DevLog($"Fetching metadata for: {url}");
            _meta = await _backend.FetchMetadataAsync(url);
            if (_meta is null) { Warn(L("ErrNoMeta")); return; }

            TxtMetaTitle.Text = _meta.Title;
            TxtMetaUploader.Text = $"👤 {_meta.Uploader}";
            TxtMetaDuration.Text = $"⏱ {TimeSpan.FromSeconds(_meta.Duration):mm\\:ss}";
            TagsList.ItemsSource = _meta.Tags.Take(8).Select(t => $"#{t}").ToList();
            LoadThumbnail(_meta.Thumbnail);

            TxtTitle.Text = _meta.Title;
            TxtTitleDl.Text = _meta.Title;
            TxtDescription.Text = _meta.Description;
            DevLog($"Metadata loaded: \"{_meta.Title}\" by {_meta.Uploader} ({_meta.Duration:F0}s, {_meta.Tags.Count} tags)");

            AnimatePanel(BorderMeta, true);
            SetStatus(L("StatusMetaLoaded"), true);
        }
        catch (Exception ex)
        {
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

            if (bmp.IsDownloading)
                bmp.DownloadCompleted += (_, _) => ApplyDominantColor(bmp);
            else
                ApplyDominantColor(bmp);

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

    private void ApplyDominantColor(BitmapSource bmp)
    {
        try
        {
            var color = GetDominantColor(bmp);
            var baseCard = ((SolidColorBrush)FindResource("BgCard")).Color;

            // Gradient: dominant tint at top (~28%) fading to base card at bottom
            const double a = 0.28;
            var cardTint = Color.FromRgb(
                (byte)(baseCard.R * (1 - a) + color.R * a),
                (byte)(baseCard.G * (1 - a) + color.G * a),
                (byte)(baseCard.B * (1 - a) + color.B * a));

            if (BorderMeta.Background is LinearGradientBrush gradient)
            {
                FluidMotion.AnimateGradientStop(gradient.GradientStops[0], cardTint);
                FluidMotion.AnimateGradientStop(gradient.GradientStops[1], baseCard);
            }
            else
            {
                gradient = new LinearGradientBrush
                {
                    StartPoint = new Point(0.5, 0),
                    EndPoint = new Point(0.5, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(baseCard, 0.0),
                        new GradientStop(baseCard, 1.0)
                    }
                };
                BorderMeta.Background = gradient;
                FluidMotion.AnimateGradientStop(gradient.GradientStops[0], cardTint);
            }

            // Stronger tint for the thumbnail container (visible in letterbox gaps)
            var baseElev = ((SolidColorBrush)FindResource("BgElevated")).Color;
            const double b = 0.40;
            var thumbBg = Color.FromRgb(
                (byte)(baseElev.R * (1 - b) + color.R * b),
                (byte)(baseElev.G * (1 - b) + color.G * b),
                (byte)(baseElev.B * (1 - b) + color.B * b));

            if (ThumbBgBorder.Background is not SolidColorBrush thumbBrush || thumbBrush.IsFrozen)
            {
                thumbBrush = new SolidColorBrush(baseElev);
                ThumbBgBorder.Background = thumbBrush;
            }
            FluidMotion.AnimateColor(thumbBrush, thumbBg);
        }
        catch { /* pixel read failed – keep default bg */ }
    }

    private void ResetThumbBackground()
    {
        var cardDefault = ((SolidColorBrush)FindResource("BgCard")).Color;
        if (BorderMeta.Background is LinearGradientBrush gradient)
        {
            FluidMotion.AnimateGradientStop(gradient.GradientStops[0], cardDefault);
            FluidMotion.AnimateGradientStop(gradient.GradientStops[1], cardDefault);
        }
        else if (BorderMeta.Background is SolidColorBrush cardBrush && !cardBrush.IsFrozen)
            FluidMotion.AnimateColor(cardBrush, cardDefault);
        else
            BorderMeta.Background = new SolidColorBrush(cardDefault);

        var thumbDefault = ((SolidColorBrush)FindResource("BgElevated")).Color;
        if (ThumbBgBorder.Background is SolidColorBrush thumbBrush && !thumbBrush.IsFrozen)
            FluidMotion.AnimateColor(thumbBrush, thumbDefault);
        else
            ThumbBgBorder.Background = new SolidColorBrush(thumbDefault);
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

        BtnStart.IsEnabled = false;
        BtnFetch.IsEnabled = false;
        HideOpenFolderBar();
        _lastDownloadedFolder = null;

        _lastJobLog = string.Empty;
        _lastLogEntry = string.Empty;
        _logViewer?.Close();

        _cts = new CancellationTokenSource();
        _progressWin = new ProgressWindow(_cts, GetBtnRect(BtnStart)) { Owner = this };
        _progressWin.Closed += (_, _) =>
        {
            _lastJobLog = _progressWin?.LogContent ?? string.Empty;
            _progressWin = null;
            var hasLog = !string.IsNullOrEmpty(_lastJobLog);
            BtnMainLog.IsEnabled = hasLog;
            if (!hasLog) BtnMainLog.Visibility = Visibility.Collapsed;
            // Always send cancel to the backend – it ignores already-completed jobs.
            if (_currentJobId != null)
                _ = _backend.CancelJobAsync(_currentJobId);
            if (!_cts.IsCancellationRequested)
                _cts.Cancel();
        };
        BtnMainLog.IsEnabled = true;
        ShowLogButton();
        _progressWin.Show();
        _progressWin.UpdateProgress(0, "Starte…", "Verbinde mit Backend…");
        _progressWin.AppendLog("Verbinde mit Backend…");

        try
        {
            DevLog($"Creating job: mode={( isUpload ? "upload" : "download" )}, fingerprint={fingerprint} ({fingerprintMethod}), quality={quality}");
            _currentJobId = await _backend.CreateJobAsync(
                url,
                isUpload ? "upload" : "download",
                "auto",
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
                    _progressWin?.UpdateProgress(status.Progress, status.Message, detail, status.Eta);
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
                        _progressWin?.AppendLog("[OK] Abgeschlossen.");
                        SetStatus(L("StatusCompleted"), true);

                        string? folderPath = null;
                        if (status.Result?.TryGetValue("file_path", out var fp) == true && fp is System.Text.Json.JsonElement je)
                            folderPath = System.IO.Path.GetDirectoryName(je.GetString());
                        else if (!string.IsNullOrWhiteSpace(TxtOutputDir.Text))
                            folderPath = TxtOutputDir.Text.Trim();

                        _progressWin?.MarkDone(true, folderPath);

                            _lastDownloadedFolder = folderPath;
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
            BtnStart.IsEnabled = true;
            BtnFetch.IsEnabled = true;
        }
    }

    // ════════════════════════════════════════════════════════════
    //  LANGUAGE
    // ════════════════════════════════════════════════════════════
    private static string L(string key)
        => Application.Current.Resources[key] as string ?? key;

    private void LangDE_Click(object s, RoutedEventArgs e) => SetLanguage("de");
    private void LangEN_Click(object s, RoutedEventArgs e) => SetLanguage("en");

    private void SetLanguage(string lang)
    {
        if (_currentLang == lang) return;
        _currentLang = lang;

        BtnLangDE.Foreground = lang == "de"
            ? (Brush)Application.Current.Resources["Accent"]
            : (Brush)Application.Current.Resources["TextSec"];
        BtnLangEN.Foreground = lang == "en"
            ? (Brush)Application.Current.Resources["Accent"]
            : (Brush)Application.Current.Resources["TextSec"];

        var dicts = Application.Current.Resources.MergedDictionaries;
        var old = dicts.FirstOrDefault(d => d.Contains("LangCode"));
        if (old != null) dicts.Remove(old);

        var src = lang == "en" ? "Assets/Strings.en.xaml" : "Assets/Strings.de.xaml";
        dicts.Add(new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/ReelsConverterUI;component/{src}")
        });
    }

    // ════════════════════════════════════════════════════════════
    //  SETTINGS
    // ════════════════════════════════════════════════════════════
    private void Settings_Click(object s, RoutedEventArgs e)
    {
        var win = new SettingsWindow(GetBtnRect((UIElement)s)) { Owner = this };
        if (win.ShowDialog() == true)
            ApplySettings();
    }

    // ════════════════════════════════════════════════════════════
    //  VIDEO EDITOR
    // ════════════════════════════════════════════════════════════
    private void Editor_Click(object s, RoutedEventArgs e)
    {
        var win = new EditorWindow(GetBtnRect((UIElement)s)) { Owner = this };
        win.ShowDialog();
    }

    // ════════════════════════════════════════════════════════════
    //  THEME DESIGNER
    // ════════════════════════════════════════════════════════════
    private void Designer_Click(object s, RoutedEventArgs e)
    {
        var win = new DesignerWindow(GetBtnRect((UIElement)s)) { Owner = this };
        win.ShowDialog();
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
        if (!s.DevConsoleEnabled) _devConsoleWin?.Close();
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
        if (BtnOpenFileLocation.Visibility == Visibility.Visible) return;
        if (RbDownload?.IsChecked != true) return;

        BtnOpenFileLocation.Visibility = Visibility.Visible;
        BtnOpenFileLocation.Opacity = 0;
        BtnOpenFileLocation.RenderTransformOrigin = new Point(0.5, 0.5);
        var st = new ScaleTransform(0.92, 0.92);
        BtnOpenFileLocation.RenderTransform = st;

        var spring = AppleSpringEase.Interactive;
        var smooth = AppleSpringEase.Gentle;

        BtnOpenFileLocation.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
            { EasingFunction = smooth });
        st.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(450))
            { EasingFunction = spring });
        st.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(450))
            { EasingFunction = spring });
    }

    private void HideOpenFolderBar()
    {
        if (BtnOpenFileLocation.Visibility != Visibility.Visible) return;

        var ease = AppleSpringEase.Snappy;
        var dur = TimeSpan.FromMilliseconds(200);

        BtnOpenFileLocation.RenderTransformOrigin = new Point(0.5, 0.5);
        var group = new TransformGroup();
        var st = new ScaleTransform(1, 1);
        var tt = new TranslateTransform(0, 0);
        group.Children.Add(st);
        group.Children.Add(tt);
        BtnOpenFileLocation.RenderTransform = group;

        var opAnim = new DoubleAnimation(1, 0, dur) { EasingFunction = ease };
        opAnim.Completed += (_, _) =>
        {
            BtnOpenFileLocation.Visibility = Visibility.Collapsed;
        };

        BtnOpenFileLocation.BeginAnimation(UIElement.OpacityProperty, opAnim);
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

    private static void AnimatePopupIn(Border border)
    {
        var spring = AppleSpringEase.Interactive;
        var smooth = AppleSpringEase.Gentle;
        var group = (TransformGroup)border.RenderTransform;
        var st = (ScaleTransform)group.Children[0];
        var tt = (TranslateTransform)group.Children[1];

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
    }

    private static void AnimatePopupOut(Border border, Action onDone)
    {
        var ease = AppleSpringEase.Snappy;
        var group = (TransformGroup)border.RenderTransform;
        var st = (ScaleTransform)group.Children[0];
        var tt = (TranslateTransform)group.Children[1];

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
        if (!SettingsService.Current.DevConsoleEnabled) return;
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        if (_devConsoleWin != null)
        {
            _devConsoleWin.AppendLog(line);
        }
        else if (BorderDevConsole.Visibility == Visibility.Visible)
        {
            TxtDevConsole.AppendText(TxtDevConsole.Text.Length == 0 ? line : Environment.NewLine + line);
            TxtDevConsole.ScrollToEnd();
        }
    }

    private void ClearDevConsole_Click(object s, RoutedEventArgs e)
        => TxtDevConsole.Clear();

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
        var lower = cmd.ToLowerInvariant();
        switch (lower)
        {
            case "help":
                DevLog("Available commands:");
                DevLog("  help        - Show this help");
                DevLog("  clear / cls - Clear the console");
                DevLog("  status      - Show backend status");
                DevLog("  info        - Show app info");
                DevLog("  Any other input is sent to the backend process stdin");
                break;
            case "clear":
            case "cls":
                TxtDevConsole.Clear();
                if (_devConsoleWin != null)
                {
                    _devConsoleWin.ClearConsole();
                }
                break;
            case "status":
                DevLog($"Backend ready: {_backendReady}");
                DevLog($"Backend URL: {SettingsService.Current.BackendUrl}");
                break;
            case "info":
                DevLog($"ReelsConverter v3.0");
                DevLog($"Language: {_currentLang}");
                DevLog($"Dev console: enabled");
                break;
            default:
                _launcher.SendInput(cmd);
                DevLog($"[sent to backend] {cmd}");
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

    private void DetachDevConsole_Click(object s, RoutedEventArgs e)
    {
        if (_devConsoleWin != null) { _devConsoleWin.Activate(); return; }
        var originRect = GetBtnRect((UIElement)s);
        _devConsoleWin = new DevConsoleWindow(TxtDevConsole.Text, originRect) { Owner = this };
        _devConsoleWin.CommandEntered += HandleConsoleCommand;
        _devConsoleWin.Closed += (_, _) =>
        {
            var reattach = _devConsoleWin?.ReattachRequested == true;
            _devConsoleWin = null;
            if (reattach && SettingsService.Current.DevConsoleEnabled)
                BorderDevConsole.Visibility = Visibility.Visible;
        };
        BorderDevConsole.Visibility = Visibility.Collapsed;
        _devConsoleWin.Show();
    }

    private void SetStatus(string text, bool ok)
    {
        TxtStatusBar.Text = text;
        var color = ok ? Color.FromRgb(0x5A, 0xAF, 0x6E) : Color.FromRgb(0xC4, 0x48, 0x48);
        FluidMotion.AnimateColor(StatusDotColor, color);
    }

    private static void Warn(string msg)
        => MessageBox.Show(msg, "ReelsConverter", MessageBoxButton.OK, MessageBoxImage.Warning);

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

    }
