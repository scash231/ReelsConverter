using ReelsConverterUI.Animations;
using ReelsConverterUI.Services;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Windows.Threading;

namespace ReelsConverterUI;

public partial class EditorWindow : Window
{
    private readonly Rect _originRect;
    private bool _isAnimatingClose;
    private string? _filePath;
    private TimeSpan _duration;
    private TimeSpan _trimStart;
    private TimeSpan _trimEnd;
    private bool _isPlaying;
    private bool _isMuted;
    private bool _isDraggingSlider;
    private string? _srtContent;
    private readonly BackendService _backend = new(SettingsService.Current.BackendUrl);
    private readonly DispatcherTimer _positionTimer;
    private int _rotationAngle = 0;
    private bool _flipHorizontal = false;
    private DateTime _lastSeekTime = DateTime.MinValue;
    private string _selectedCropRatio = "Original";
    private double _selectedSpeed = 1.0;
    private double _volumeFactor = 1.0;
    private int _currentTabIndex = 0;

    public EditorWindow(Rect originRect, string? initialFile = null)
    {
        InitializeComponent();
        _originRect = originRect;
        _filePath = initialFile;

        _positionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _positionTimer.Tick += PositionTimer_Tick;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (SettingsService.Current.BlurEditor)
        {
            Services.WindowBlurHelper.EnableBlurWithFade(this, RootBorder);
        }
        FluidMotion.MorphOpen(RootBorder, WindowScale, WindowTranslate, _originRect, this);

        if (!string.IsNullOrEmpty(_filePath) && File.Exists(_filePath))
            LoadVideo(_filePath);
    }

    // ════════════════════════════════════════════════════════════
    //  TITLEBAR
    // ════════════════════════════════════════════════════════════
    private void TitleBar_Drag(object s, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object s, RoutedEventArgs e)
        => CloseWithAnimation();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isAnimatingClose) { e.Cancel = true; CloseWithAnimation(); }
        base.OnClosing(e);
    }

    private void CloseWithAnimation()
    {
        if (_isAnimatingClose) return;
        _isAnimatingClose = true;
        _positionTimer.Stop();
        VideoPlayer.Stop();
        VideoPlayer.Close();
        _backend.Dispose();
        FluidMotion.MorphClose(RootBorder, WindowScale, WindowTranslate, _originRect, this,
            () => {
                try
                {
                    DialogResult = false;
                }
                catch (System.InvalidOperationException)
                {
                    Close();
                }
            });
    }

    // ════════════════════════════════════════════════════════════
    //  FILE LOADING
    // ════════════════════════════════════════════════════════════
    private void BrowseVideo_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = L("EditorBrowseTitle"),
            Filter = "Video|*.mp4;*.mkv;*.avi;*.mov;*.webm;*.wmv|All Files|*.*"
        };
        if (dlg.ShowDialog() == true)
            LoadVideo(dlg.FileName);
    }

    public void LoadVideo(string path)
    {
        _filePath = path;
        TxtFileName.Text = Path.GetFileName(path);
        TxtNoVideo.Visibility = Visibility.Collapsed;

        _rotationAngle = 0;
        _flipHorizontal = false;
        UpdatePreviewTransform();

        VideoPlayer.Source = new Uri(path, UriKind.Absolute);
        VideoPlayer.Play();
        VideoPlayer.Pause();
        _isPlaying = false;
        TxtPlayIcon.Text = "▶";
    }

    // ════════════════════════════════════════════════════════════
    //  MEDIA EVENTS
    // ════════════════════════════════════════════════════════════
    private void VideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (!VideoPlayer.NaturalDuration.HasTimeSpan) return;

        _duration = VideoPlayer.NaturalDuration.TimeSpan;
        _trimStart = TimeSpan.Zero;
        _trimEnd = _duration;

        SliderTimeline.Maximum = _duration.TotalSeconds;
        SliderTimeline.SelectionStart = 0;
        SliderTimeline.SelectionEnd = _duration.TotalSeconds;
        SliderTimeline.IsEnabled = true;
        TxtDuration.Text = FormatTime(_duration);
        TxtTrimStart.Text = FormatTime(_trimStart);
        TxtTrimEnd.Text = FormatTime(_trimEnd);

        // Enable all controls
        BtnPlay.IsEnabled = true;
        BtnStop.IsEnabled = true;
        BtnMuteToggle.IsEnabled = true;
        TxtTrimStart.IsEnabled = true;
        TxtTrimEnd.IsEnabled = true;
        BtnSetStart.IsEnabled = true;
        BtnSetEnd.IsEnabled = true;
        BtnResetTrim.IsEnabled = true;
        ChkMuteAudio.IsEnabled = true;
        BtnGenSubs.IsEnabled = true;
        BtnExport.IsEnabled = true;

        BtnRotateCCW.IsEnabled = true;
        BtnRotateCW.IsEnabled = true;
        BtnFlipH.IsEnabled = true;

        RadCropOriginal.IsEnabled = true;
        RadCrop916.IsEnabled = true;
        RadCrop169.IsEnabled = true;
        RadCrop11.IsEnabled = true;

        RadSpeed05.IsEnabled = true;
        RadSpeed10.IsEnabled = true;
        RadSpeed15.IsEnabled = true;
        RadSpeed20.IsEnabled = true;

        SliderVolume.IsEnabled = true;
        CmbResolution.IsEnabled = true;
        CmbFormat.IsEnabled = true;

        UpdateCropOverlayBounds();
    }

    private void VideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        _isPlaying = false;
        TxtPlayIcon.Text = "▶";
        _positionTimer.Stop();
        VideoPlayer.Position = _trimStart;
        SliderTimeline.Value = _trimStart.TotalSeconds;
        TxtCurrentTime.Text = FormatTime(_trimStart);
    }

    // ════════════════════════════════════════════════════════════
    //  PLAYBACK CONTROLS
    // ════════════════════════════════════════════════════════════
    private void Play_Click(object s, RoutedEventArgs e)
    {
        if (_isPlaying)
        {
            VideoPlayer.Pause();
            _positionTimer.Stop();
            _isPlaying = false;
            TxtPlayIcon.Text = "▶";
        }
        else
        {
            VideoPlayer.Play();
            _positionTimer.Start();
            _isPlaying = true;
            TxtPlayIcon.Text = "⏸";
        }
    }

    private void Stop_Click(object s, RoutedEventArgs e)
    {
        VideoPlayer.Pause();
        VideoPlayer.Position = _trimStart;
        _positionTimer.Stop();
        _isPlaying = false;
        TxtPlayIcon.Text = "▶";
        SliderTimeline.Value = _trimStart.TotalSeconds;
        TxtCurrentTime.Text = FormatTime(_trimStart);
    }

    private void MuteToggle_Click(object s, RoutedEventArgs e)
    {
        _isMuted = !_isMuted;
        VideoPlayer.IsMuted = _isMuted;
        TxtMuteIcon.Text = _isMuted ? "🔇" : "🔊";
    }

    private void PositionTimer_Tick(object? sender, EventArgs e)
    {
        if (_isDraggingSlider) return;
        var pos = VideoPlayer.Position;
        if (pos >= _trimEnd)
        {
            VideoPlayer_MediaEnded(this, new RoutedEventArgs());
            return;
        }
        SliderTimeline.Value = pos.TotalSeconds;
        TxtCurrentTime.Text = FormatTime(pos);
    }

    private void SliderTimeline_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isDraggingSlider)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastSeekTime).TotalMilliseconds >= 50)
            {
                _lastSeekTime = now;
                VideoPlayer.Position = TimeSpan.FromSeconds(e.NewValue);
                TxtCurrentTime.Text = FormatTime(VideoPlayer.Position);
            }
        }
    }

    private void SliderTimeline_DragStarted(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _isDraggingSlider = true;
    }

    private void SliderTimeline_DragCompleted(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _isDraggingSlider = false;
        VideoPlayer.Position = TimeSpan.FromSeconds(SliderTimeline.Value);
    }

    // ════════════════════════════════════════════════════════════
    //  TRIM CONTROLS
    // ════════════════════════════════════════════════════════════
    private void SetStart_Click(object s, RoutedEventArgs e)
    {
        _trimStart = VideoPlayer.Position;
        TxtTrimStart.Text = FormatTime(_trimStart);
        SliderTimeline.SelectionStart = _trimStart.TotalSeconds;
    }

    private void SetEnd_Click(object s, RoutedEventArgs e)
    {
        _trimEnd = VideoPlayer.Position;
        TxtTrimEnd.Text = FormatTime(_trimEnd);
        SliderTimeline.SelectionEnd = _trimEnd.TotalSeconds;
    }

    private void ResetTrim_Click(object s, RoutedEventArgs e)
    {
        _trimStart = TimeSpan.Zero;
        _trimEnd = _duration;
        TxtTrimStart.Text = FormatTime(_trimStart);
        TxtTrimEnd.Text = FormatTime(_trimEnd);
        SliderTimeline.SelectionStart = 0;
        SliderTimeline.SelectionEnd = _duration.TotalSeconds;
        
        _rotationAngle = 0;
        _flipHorizontal = false;
        UpdatePreviewTransform();
    }

    // ════════════════════════════════════════════════════════════
    //  SUBTITLES
    // ════════════════════════════════════════════════════════════
    private async void GenSubs_Click(object s, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_filePath)) return;

        BtnGenSubs.IsEnabled = false;
        BtnGenSubs.Content = L("EditorGenSubsRunning");
        PbSubs.Visibility = Visibility.Visible;
        TxtSubStatus.Text = L("EditorSubStatusGenerating");
        TxtSubStatus.Visibility = Visibility.Visible;

        try
        {
            var model = (CmbSubModel.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "base";
            var lang = (CmbSubLang.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "auto";

            _srtContent = await _backend.GenerateSubtitlesAsync(_filePath, model, lang);

            if (string.IsNullOrWhiteSpace(_srtContent))
            {
                TxtSubStatus.Text = L("EditorSubStatusEmpty");
                return;
            }

            TxtSubPreview.Text = _srtContent;
            TxtSubPreview.Visibility = Visibility.Visible;
            ChkBurnSubs.IsEnabled = true;
            ChkBurnSubs.IsChecked = true;
            TxtSubStatus.Text = L("EditorSubStatusDone");
        }
        catch (Exception ex)
        {
            TxtSubStatus.Text = $"{L("ErrPrefix")} {ex.Message}";
        }
        finally
        {
            PbSubs.Visibility = Visibility.Collapsed;
            BtnGenSubs.IsEnabled = true;
            BtnGenSubs.Content = L("EditorGenSubs");
        }
    }

    // ════════════════════════════════════════════════════════════
    //  EXPORT
    // ════════════════════════════════════════════════════════════
    private async void Export_Click(object s, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_filePath)) return;

        var formatItem = CmbFormat.SelectedItem as ComboBoxItem;
        var formatTag = formatItem?.Tag?.ToString() ?? "mp4";
        
        string filter = "MP4 Video|*.mp4";
        string defaultExt = "mp4";
        if (formatTag == "mkv")
        {
            filter = "MKV Video|*.mkv";
            defaultExt = "mkv";
        }
        else if (formatTag == "webm")
        {
            filter = "WebM Video|*.webm";
            defaultExt = "webm";
        }

        var dlg = new SaveFileDialog
        {
            Title = L("EditorExportTitle"),
            Filter = filter,
            DefaultExt = defaultExt,
            FileName = Path.GetFileNameWithoutExtension(_filePath) + "_edited." + defaultExt
        };
        if (dlg.ShowDialog() != true) return;

        // Prepare subtitle temp file if needed
        string? srtPath = null;
        bool burnSubs = ChkBurnSubs.IsChecked == true && !string.IsNullOrEmpty(_srtContent);
        if (burnSubs)
        {
            srtPath = Path.Combine(Path.GetTempPath(), $"reels_subs_{Guid.NewGuid():N}.srt");
            await File.WriteAllTextAsync(srtPath, TxtSubPreview.Text);
        }

        BtnExport.IsEnabled = false;
        BtnExport.Content = L("EditorExporting");

        try
        {
            var output = dlg.FileName;
            var args = BuildFfmpegArgs(_filePath, output, srtPath);
            var trimDurationSecs = (_trimEnd - _trimStart).TotalSeconds;

            var mainWin = Owner as MainWindow;
            if (mainWin != null)
            {
                // Start the export job asynchronously on MainWindow
                _ = mainWin.RunFfmpegExportJobAsync(output, args, srtPath, trimDurationSecs);
                // Close EditorWindow since the progress is shown on MainWindow
                Close();
            }
            else
            {
                // Fallback to local Task if MainWindow is not the owner
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
                    proc?.WaitForExit();
                    if (proc?.ExitCode != 0)
                    {
                        var err = proc?.StandardError.ReadToEnd();
                        throw new Exception($"ffmpeg exited with code {proc?.ExitCode}: {err?[..Math.Min(err.Length, 200)]}");
                    }
                });
                NotificationWindow.Show(L("EditorExportDone"), this, NotificationType.Info);
            }
        }
        catch (Exception ex)
        {
            NotificationWindow.Show($"{L("ErrPrefix")} {ex.Message}", this, NotificationType.Error);
            if (srtPath != null && File.Exists(srtPath))
                try { File.Delete(srtPath); } catch { }
        }
        finally
        {
            var mainWin = Owner as MainWindow;
            if (mainWin == null)
            {
                if (srtPath != null && File.Exists(srtPath))
                    try { File.Delete(srtPath); } catch { }
                BtnExport.IsEnabled = true;
                BtnExport.Content = L("EditorExport");
            }
        }
    }

    private void RotateCCW_Click(object sender, RoutedEventArgs e)
    {
        _rotationAngle = (_rotationAngle + 270) % 360;
        UpdatePreviewTransform();
    }

    private void RotateCW_Click(object sender, RoutedEventArgs e)
    {
        _rotationAngle = (_rotationAngle + 90) % 360;
        UpdatePreviewTransform();
    }

    private void FlipH_Click(object sender, RoutedEventArgs e)
    {
        _flipHorizontal = !_flipHorizontal;
        UpdatePreviewTransform();
    }

    private void UpdatePreviewTransform()
    {
        var group = new TransformGroup();
        if (_flipHorizontal)
        {
            group.Children.Add(new ScaleTransform(-1, 1));
        }
        if (_rotationAngle != 0)
        {
            group.Children.Add(new RotateTransform(_rotationAngle));
        }
        VideoPlayer.RenderTransformOrigin = new Point(0.5, 0.5);
        VideoPlayer.RenderTransform = group;
        UpdateCropOverlayBounds();
    }

    private void InspectorTab_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb)
        {
            var tabName = rb.Name;
            int newIndex = tabName switch
            {
                "TabVideo" => 0,
                "TabAudio" => 1,
                "TabSubs" => 2,
                "TabExport" => 3,
                _ => 0
            };

            Border? targetBorder = tabName switch
            {
                "TabVideo" => InspectorVideo,
                "TabAudio" => InspectorAudio,
                "TabSubs" => InspectorSubs,
                "TabExport" => InspectorExport,
                _ => null
            };

            if (targetBorder == null) return;

            if (!this.IsLoaded)
            {
                if (InspectorVideo != null) InspectorVideo.Visibility = Visibility.Collapsed;
                if (InspectorAudio != null) InspectorAudio.Visibility = Visibility.Collapsed;
                if (InspectorSubs != null) InspectorSubs.Visibility = Visibility.Collapsed;
                if (InspectorExport != null) InspectorExport.Visibility = Visibility.Collapsed;

                targetBorder.Visibility = Visibility.Visible;
                _currentTabIndex = newIndex;
                return;
            }

            Border? currentBorder = _currentTabIndex switch
            {
                0 => InspectorVideo,
                1 => InspectorAudio,
                2 => InspectorSubs,
                3 => InspectorExport,
                _ => null
            };

            // Collapse other panels to prevent rapid clicking from showing multiple overlapping panels
            if (InspectorVideo != null && InspectorVideo != targetBorder && InspectorVideo != currentBorder) InspectorVideo.Visibility = Visibility.Collapsed;
            if (InspectorAudio != null && InspectorAudio != targetBorder && InspectorAudio != currentBorder) InspectorAudio.Visibility = Visibility.Collapsed;
            if (InspectorSubs != null && InspectorSubs != targetBorder && InspectorSubs != currentBorder) InspectorSubs.Visibility = Visibility.Collapsed;
            if (InspectorExport != null && InspectorExport != targetBorder && InspectorExport != currentBorder) InspectorExport.Visibility = Visibility.Collapsed;

            if (currentBorder != null && currentBorder != targetBorder)
            {
                double direction = (newIndex > _currentTabIndex) ? 1.0 : -1.0;
                FluidMotion.LiquidGlassCrossfade(currentBorder, targetBorder, direction);
            }
            else
            {
                targetBorder.Visibility = Visibility.Visible;
            }

            _currentTabIndex = newIndex;
        }
    }

    private void CropRatio_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb)
        {
            _selectedCropRatio = rb.Content.ToString() ?? "Original";
            UpdateCropOverlayBounds();
        }
    }

    private void Speed_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb)
        {
            var speedText = rb.Content.ToString()?.Replace("x", "") ?? "1.0";
            if (double.TryParse(speedText, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
            {
                _selectedSpeed = val;
                VideoPlayer.SpeedRatio = _selectedSpeed;
                TxtSpeedBadge.Text = $"{_selectedSpeed:F1}x";
            }
        }
    }

    private void SliderVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _volumeFactor = e.NewValue;
        if (VideoPlayer != null)
        {
            VideoPlayer.Volume = Math.Min(1.0, _volumeFactor);
        }
        if (TxtVolumeVal != null)
        {
            TxtVolumeVal.Text = $"{(int)(_volumeFactor * 100)}%";
        }
        if (TxtVolumeBadge != null)
        {
            TxtVolumeBadge.Text = $"{(int)(_volumeFactor * 100)}%";
        }
    }

    private void VideoPlayer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCropOverlayBounds();
    }

    private void UpdateCropOverlayBounds()
    {
        if (VideoPlayer == null || CropOverlay == null) return;
        if (!VideoPlayer.NaturalDuration.HasTimeSpan || VideoPlayer.NaturalVideoWidth == 0 || VideoPlayer.NaturalVideoHeight == 0)
        {
            CropOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        if (_selectedCropRatio == "Original")
        {
            CropOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        double natW = VideoPlayer.NaturalVideoWidth;
        double natH = VideoPlayer.NaturalVideoHeight;

        if (_rotationAngle == 90 || _rotationAngle == 270)
        {
            natW = VideoPlayer.NaturalVideoHeight;
            natH = VideoPlayer.NaturalVideoWidth;
        }

        double playerW = VideoPlayer.ActualWidth;
        double playerH = VideoPlayer.ActualHeight;

        if (playerW == 0 || playerH == 0)
        {
            CropOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        double rVideo = natW / natH;
        double rPlayer = playerW / playerH;

        double renderW, renderH;
        double renderX = 0;
        double renderY = 0;

        if (rVideo > rPlayer)
        {
            renderW = playerW;
            renderH = playerW / rVideo;
            renderY = (playerH - renderH) / 2;
        }
        else
        {
            renderW = playerH * rVideo;
            renderH = playerH;
            renderX = (playerW - renderW) / 2;
        }

        double rTarget = 1.0;
        if (_selectedCropRatio == "9:16") rTarget = 9.0 / 16.0;
        else if (_selectedCropRatio == "16:9") rTarget = 16.0 / 9.0;
        else if (_selectedCropRatio == "1:1") rTarget = 1.0;

        double cropW, cropH;
        if (rVideo > rTarget)
        {
            cropH = renderH;
            cropW = renderH * rTarget;
        }
        else
        {
            cropW = renderW;
            cropH = renderW / rTarget;
        }

        double cropX = (renderW - cropW) / 2 + renderX;
        double cropY = (renderH - cropH) / 2 + renderY;

        CropOverlay.Width = cropW;
        CropOverlay.Height = cropH;
        CropOverlay.Margin = new Thickness(cropX, cropY, 0, 0);
        CropOverlay.Visibility = Visibility.Visible;
    }

    private string BuildFfmpegArgs(string input, string output, string? srtPath = null)
    {
        var parts = new List<string> { "-y" };

        if (_trimStart > TimeSpan.Zero)
            parts.AddRange(["-ss", _trimStart.TotalSeconds.ToString("F3")]);

        parts.AddRange(["-i", $"\"{input}\""]);

        var trimDuration = _trimEnd - _trimStart;
        if (trimDuration < _duration)
            parts.AddRange(["-t", trimDuration.TotalSeconds.ToString("F3")]);

        var resItem = CmbResolution.SelectedItem as ComboBoxItem;
        var resTag = resItem?.Tag?.ToString() ?? "original";

        var formatItem = CmbFormat.SelectedItem as ComboBoxItem;
        var formatTag = formatItem?.Tag?.ToString() ?? "mp4";

        bool hasFilters = (_rotationAngle != 0) || 
                          _flipHorizontal || 
                          !string.IsNullOrEmpty(srtPath) || 
                          (_selectedCropRatio != "Original") || 
                          (_selectedSpeed != 1.0) || 
                          (_volumeFactor != 1.0) || 
                          (resTag != "original") ||
                          (formatTag == "webm");

        if (!hasFilters)
        {
            if (ChkMuteAudio.IsChecked == true)
            {
                parts.AddRange(["-c:v", "copy", "-an"]);
            }
            else
            {
                parts.AddRange(["-c:v", "copy", "-c:a", "copy"]);
            }
        }
        else
        {
            var vf = new List<string>();

            if (_rotationAngle == 90) vf.Add("transpose=1");
            else if (_rotationAngle == 180) vf.Add("transpose=1,transpose=1");
            else if (_rotationAngle == 270) vf.Add("transpose=2");

            if (_flipHorizontal) vf.Add("hflip");

            if (_selectedCropRatio != "Original")
            {
                double transW = VideoPlayer.NaturalVideoWidth;
                double transH = VideoPlayer.NaturalVideoHeight;
                if (_rotationAngle == 90 || _rotationAngle == 270)
                {
                    transW = VideoPlayer.NaturalVideoHeight;
                    transH = VideoPlayer.NaturalVideoWidth;
                }

                double rTrans = transW / transH;
                double rTarget = 1.0;
                if (_selectedCropRatio == "9:16") rTarget = 9.0 / 16.0;
                else if (_selectedCropRatio == "16:9") rTarget = 16.0 / 9.0;
                else if (_selectedCropRatio == "1:1") rTarget = 1.0;

                double cropW = transW;
                double cropH = transH;
                if (rTrans > rTarget)
                {
                    cropH = transH;
                    cropW = transH * rTarget;
                }
                else
                {
                    cropW = transW;
                    cropH = transW / rTarget;
                }

                int finalW = ((int)Math.Round(cropW)) & ~1;
                int finalH = ((int)Math.Round(cropH)) & ~1;
                int finalX = ((int)Math.Round((transW - cropW) / 2)) & ~1;
                int finalY = ((int)Math.Round((transH - cropH) / 2)) & ~1;

                vf.Add($"crop={finalW}:{finalH}:{finalX}:{finalY}");
            }

            if (!string.IsNullOrEmpty(srtPath))
            {
                var escaped = srtPath.Replace("\\", "/").Replace(":", "\\:");
                vf.Add($"subtitles='{escaped}'");
            }

            if (resTag != "original")
            {
                int targetHeight = 1080;
                if (resTag == "720p") targetHeight = 720;
                else if (resTag == "480p") targetHeight = 480;

                vf.Add($"scale=-2:{targetHeight}");
            }

            if (_selectedSpeed != 1.0)
            {
                vf.Add($"setpts=PTS/{_selectedSpeed.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");
            }

            if (vf.Count > 0)
                parts.AddRange(["-vf", $"\"{string.Join(",", vf)}\""]);

            if (ChkMuteAudio.IsChecked == true)
            {
                parts.Add("-an");
            }
            else
            {
                var af = new List<string>();

                if (_selectedSpeed != 1.0)
                {
                    af.Add($"atempo={_selectedSpeed.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");
                }

                if (_volumeFactor != 1.0)
                {
                    af.Add($"volume={_volumeFactor.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");
                }

                if (af.Count > 0)
                {
                    parts.AddRange(["-af", $"\"{string.Join(",", af)}\""]);
                }

                if (formatTag == "webm")
                {
                    parts.AddRange(["-c:a", "libopus", "-b:a", "96k"]);
                }
                else
                {
                    parts.AddRange(["-c:a", "aac", "-b:a", "128k"]);
                }
            }

            if (formatTag == "webm")
            {
                parts.AddRange(["-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0", "-deadline", "realtime"]);
            }
            else
            {
                if (SettingsService.Current.UseGpu)
                {
                    parts.AddRange(["-c:v", "h264_nvenc", "-cq", "20", "-preset", "fast"]);
                }
                else
                {
                    parts.AddRange(["-c:v", "libx264", "-crf", "18", "-preset", "fast"]);
                }
            }
        }

        parts.AddRange(["-movflags", "+faststart"]);
        parts.Add($"\"{output}\"");

        return string.Join(" ", parts);
    }

    // ════════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════════
    private static string FormatTime(TimeSpan t)
        => t.TotalHours >= 1
            ? t.ToString(@"h\:mm\:ss")
            : t.ToString(@"m\:ss");

    private static string L(string key)
        => Application.Current.Resources[key] as string ?? key;

    private static string FindFfmpegExe()
    {
        // 1. Check beside ProcessPath (published scenarios)
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (exeDir is not null)
        {
            var localPath = Path.Combine(exeDir, "backend", "ffmpeg_bin", "ffmpeg.exe");
            if (File.Exists(localPath)) return localPath;
            
            var localPath2 = Path.Combine(exeDir, "ffmpeg_bin", "ffmpeg.exe");
            if (File.Exists(localPath2)) return localPath2;
        }

        // 2. Climb up parent directories (development scenarios)
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            var path1 = Path.Combine(dir, "backend", "ffmpeg_bin", "ffmpeg.exe");
            if (File.Exists(path1)) return path1;

            var path2 = Path.Combine(dir, "ffmpeg_bin", "ffmpeg.exe");
            if (File.Exists(path2)) return path2;

            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }

        // 3. Fallback to path resolution
        return "ffmpeg";
    }
}
