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
            () => { DialogResult = false; });
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

    private void LoadVideo(string path)
    {
        _filePath = path;
        TxtFileName.Text = Path.GetFileName(path);
        TxtNoVideo.Visibility = Visibility.Collapsed;

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
            VideoPlayer.Position = TimeSpan.FromSeconds(e.NewValue);
            TxtCurrentTime.Text = FormatTime(VideoPlayer.Position);
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
    }

    private void SetEnd_Click(object s, RoutedEventArgs e)
    {
        _trimEnd = VideoPlayer.Position;
        TxtTrimEnd.Text = FormatTime(_trimEnd);
    }

    private void ResetTrim_Click(object s, RoutedEventArgs e)
    {
        _trimStart = TimeSpan.Zero;
        _trimEnd = _duration;
        TxtTrimStart.Text = FormatTime(_trimStart);
        TxtTrimEnd.Text = FormatTime(_trimEnd);
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

        var dlg = new SaveFileDialog
        {
            Title = L("EditorExportTitle"),
            Filter = "MP4|*.mp4",
            FileName = Path.GetFileNameWithoutExtension(_filePath) + "_edited.mp4"
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
            await Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
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

            MessageBox.Show(L("EditorExportDone"), "ReelsConverter",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{L("ErrPrefix")} {ex.Message}", "ReelsConverter",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (srtPath != null && File.Exists(srtPath))
                try { File.Delete(srtPath); } catch { }
            BtnExport.IsEnabled = true;
            BtnExport.Content = L("EditorExport");
        }
    }

    private string BuildFfmpegArgs(string input, string output, string? srtPath = null)
    {
        var parts = new List<string> { "-y" };

        // Trim: seek to start
        if (_trimStart > TimeSpan.Zero)
            parts.AddRange(["-ss", _trimStart.TotalSeconds.ToString("F3")]);

        parts.AddRange(["-i", $"\"{input}\""]);

        // Trim: duration
        var trimDuration = _trimEnd - _trimStart;
        if (trimDuration < _duration)
            parts.AddRange(["-t", trimDuration.TotalSeconds.ToString("F3")]);

        // Video filters
        var vf = new List<string>();

        // Burn subtitles into video
        if (!string.IsNullOrEmpty(srtPath))
        {
            var escaped = srtPath.Replace("\\", "/").Replace(":", "\\:");
            vf.Add($"subtitles='{escaped}'");
        }

        if (vf.Count > 0)
            parts.AddRange(["-vf", $"\"{string.Join(",", vf)}\""]);

        // Audio
        if (ChkMuteAudio.IsChecked == true)
            parts.Add("-an");
        else
            parts.AddRange(["-c:a", "aac", "-b:a", "128k"]);

        // Video codec
        parts.AddRange(["-c:v", "libx264", "-crf", "18", "-preset", "fast"]);
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
}
