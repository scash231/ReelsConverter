using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using ReelsConverterUI.Models;
using ReelsConverterUI.Services;

namespace ReelsConverterUI;

public partial class MainWindow : Window
{
    private readonly BackendLauncher _launcher = new();
    private readonly BackendService _backend = new();
    private MetadataResponse? _meta;
    private CancellationTokenSource? _cts;
    private ProgressWindow? _progressWin;
    private bool _backendReady;
    private string _currentPlatform = "instagram";

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += (_, _) => { _cts?.Cancel(); _launcher.Dispose(); _backend.Dispose(); };
    }

    // ════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ════════════════════════════════════════════════════════════
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        AnimateTabIndicator(TabInsta);
        Mode_Changed(sender, e);

        try
        {
            _launcher.Start();
            _backendReady = await _backend.WaitForHealthAsync(CancellationToken.None);
            SetStatus(_backendReady ? "Backend bereit" : "Backend nicht erreichbar",
                      _backendReady);
        }
        catch (Exception ex)
        {
            SetStatus($"Backend-Fehler: {ex.Message}", false);
        }
    }

    // ════════════════════════════════════════════════════════════
    //  TITLEBAR
    // ════════════════════════════════════════════════════════════
    private void TitleBar_Drag(object s, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void Minimize_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object s, RoutedEventArgs e) => Close();

    // ════════════════════════════════════════════════════════════
    //  TABS
    // ════════════════════════════════════════════════════════════
    private void Tab_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;

        if (TabInsta?.IsChecked == true)
        {
            _currentPlatform = "instagram";
            LblUrl.Text = "INSTAGRAM REEL URL";
            AnimateTabIndicator(TabInsta);
        }
        else if (TabTikTok?.IsChecked == true)
        {
            _currentPlatform = "tiktok";
            LblUrl.Text = "TIKTOK VIDEO URL";
            AnimateTabIndicator(TabTikTok);
        }
        else if (TabYouTube?.IsChecked == true)
        {
            _currentPlatform = "youtube";
            LblUrl.Text = "YOUTUBE VIDEO URL";
            AnimateTabIndicator(TabYouTube);
        }
    }

    private void AnimateTabIndicator(RadioButton tab)
    {
        if (tab.Parent is not StackPanel panel) return;
        var pos = tab.TranslatePoint(new Point(0, 0), panel);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var dur = TimeSpan.FromMilliseconds(250);

        var move = new DoubleAnimation(pos.X, dur) { EasingFunction = ease };
        TabIndicatorTranslate.BeginAnimation(TranslateTransform.XProperty, move);

        var widthAnim = new DoubleAnimation(tab.ActualWidth, dur) { EasingFunction = ease };
        TabIndicator.BeginAnimation(WidthProperty, widthAnim);
    }

    // ════════════════════════════════════════════════════════════
    //  MODE TOGGLE
    // ════════════════════════════════════════════════════════════
    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;

        if (RbUpload?.IsChecked == true)
        {
            AnimatePanel(BorderUpload, true);
            AnimatePanel(BorderDownload, false);
        }
        else
        {
            AnimatePanel(BorderUpload, false);
            AnimatePanel(BorderDownload, true);
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

    private void Browse_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Speicherort wählen" };
        if (dlg.ShowDialog() == true)
            TxtOutputDir.Text = dlg.FolderName;
    }

    private void EditDescription_Click(object s, RoutedEventArgs e)
    {
        var editor = new DescriptionEditorWindow(TxtDescription.Text) { Owner = this };
        if (editor.ShowDialog() == true)
            TxtDescription.Text = editor.Description;
    }

    // ════════════════════════════════════════════════════════════
    //  FETCH METADATA
    // ════════════════════════════════════════════════════════════
    private async void Fetch_Click(object s, RoutedEventArgs e)
    {
        var url = TxtUrl.Text.Trim();
        if (string.IsNullOrEmpty(url)) { Warn("Bitte eine URL eingeben."); return; }
        if (!_backendReady) { Warn("Backend nicht bereit."); return; }

        BtnFetch.IsEnabled = false;
        SetStatus("Lade Metadaten…", true);

        try
        {
            _meta = await _backend.FetchMetadataAsync(url);
            if (_meta is null) { Warn("Keine Metadaten empfangen."); return; }

            TxtMetaTitle.Text = _meta.Title;
            TxtMetaUploader.Text = $"👤 {_meta.Uploader}";
            TxtMetaDuration.Text = $"⏱ {TimeSpan.FromSeconds(_meta.Duration):mm\\:ss}";
            TagsList.ItemsSource = _meta.Tags.Take(8).Select(t => $"#{t}").ToList();

            TxtTitle.Text = _meta.Title;
            TxtDescription.Text = _meta.Description;

            AnimatePanel(BorderMeta, true);
            SetStatus("Metadaten geladen", true);
        }
        catch (Exception ex)
        {
            Warn($"Fehler: {ex.Message}");
            SetStatus("Fehler beim Laden", false);
        }
        finally
        {
            BtnFetch.IsEnabled = true;
        }
    }

    // ════════════════════════════════════════════════════════════
    //  START JOB
    // ════════════════════════════════════════════════════════════
    private async void Start_Click(object s, RoutedEventArgs e)
    {
        var url = TxtUrl.Text.Trim();
        if (string.IsNullOrEmpty(url)) { Warn("Bitte eine URL eingeben."); return; }
        if (!_backendReady) { Warn("Backend nicht bereit."); return; }

        var isUpload = RbUpload.IsChecked == true;
        if (isUpload && string.IsNullOrWhiteSpace(TxtTitle.Text))
        { Warn("Bitte einen Titel eingeben."); return; }
        if (!isUpload && string.IsNullOrWhiteSpace(TxtOutputDir.Text))
        { Warn("Bitte einen Speicherort wählen."); return; }

        var privacy = (CmbPrivacy.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "public";
        var fingerprint = isUpload ? ChkFingerprint.IsChecked == true
                                   : ChkFingerprintDl.IsChecked == true;
        var fingerprintMethod = isUpload
            ? (CmbFingerprintMethod.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "standard"
            : (CmbFingerprintMethodDl.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "standard";

        BtnStart.IsEnabled = false;
        BtnFetch.IsEnabled = false;

        _cts = new CancellationTokenSource();
        _progressWin = new ProgressWindow(_cts) { Owner = this };
        _progressWin.Closed += (_, _) => { _progressWin = null; if (!_cts.IsCancellationRequested) _cts.Cancel(); };
        _progressWin.Show();
        _progressWin.UpdateProgress(0, "Starte…", "Verbinde mit Backend…");

        try
        {
            var jobId = await _backend.CreateJobAsync(
                url,
                isUpload ? "upload" : "download",
                _currentPlatform,
                TxtTitle.Text.Trim(),
                TxtDescription.Text.Trim(),
                _meta?.Tags,
                isUpload ? null : TxtOutputDir.Text.Trim(),
                privacy,
                fingerprint,
                fingerprintMethod,
                _cts.Token);

            await foreach (var status in _backend.StreamJobAsync(jobId, _cts.Token))
            {
                var detail = status.Status == "completed" ? "🎉 Fertig!"
                           : status.Status == "error"     ? $"❌ {status.Error}"
                           : "";
                _progressWin?.UpdateProgress(status.Progress, status.Message, detail);

                if (status.Status == "completed")
                {
                    var resultUrl = status.Result?.GetValueOrDefault("url")?.ToString()
                                 ?? status.Result?.GetValueOrDefault("file_path")?.ToString()
                                 ?? "";

                    SetStatus("Erfolgreich abgeschlossen!", true);
                    _progressWin?.MarkDone(true);

                    if (!string.IsNullOrEmpty(resultUrl))
                        ShowResult(resultUrl);
                    break;
                }

                if (status.Status == "error")
                {
                    SetStatus($"Fehler: {status.Error}", false);
                    _progressWin?.MarkDone(false);
                    break;
                }
            }
        }
        catch (OperationCanceledException) { SetStatus("Abgebrochen", false); }
        catch (Exception ex)
        {
            SetStatus($"Fehler: {ex.Message}", false);
            _progressWin?.MarkDone(false);
        }
        finally
        {
            BtnStart.IsEnabled = true;
            BtnFetch.IsEnabled = true;
        }
    }

    // ════════════════════════════════════════════════════════════
    //  ANIMATIONS
    // ════════════════════════════════════════════════════════════
    private void AnimatePanel(Border panel, bool show)
    {
        var ease = new CubicEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn };
        var dur = TimeSpan.FromMilliseconds(show ? 350 : 250);

        if (show) panel.Visibility = Visibility.Visible;

        var opacityAnim = new DoubleAnimation(show ? 0 : 1, show ? 1 : 0, dur) { EasingFunction = ease };

        if (!show)
            opacityAnim.Completed += (_, _) => panel.Visibility = Visibility.Collapsed;

        panel.BeginAnimation(OpacityProperty, opacityAnim);
    }

    // ════════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════════
    private void SetStatus(string text, bool ok)
    {
        TxtStatusBar.Text = text;
        var color = ok ? Color.FromRgb(0x5A, 0xAF, 0x6E) : Color.FromRgb(0xC4, 0x48, 0x48);
        var anim = new ColorAnimation(color, TimeSpan.FromMilliseconds(300));
        StatusDotColor.BeginAnimation(SolidColorBrush.ColorProperty, anim);
    }

    private static void Warn(string msg)
        => MessageBox.Show(msg, "ReelsConverter", MessageBoxButton.OK, MessageBoxImage.Warning);

    private static void ShowResult(string result)
        => MessageBox.Show($"Ergebnis:\n{result}", "Abgeschlossen",
                           MessageBoxButton.OK, MessageBoxImage.Information);
}
