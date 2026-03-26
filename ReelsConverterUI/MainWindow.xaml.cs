using System.Linq;
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
    private readonly BackendService _backend = new(SettingsService.Current.BackendUrl);
    private MetadataResponse? _meta;
    private CancellationTokenSource? _cts;
    private ProgressWindow? _progressWin;
    private LogViewerWindow? _logViewer;
    private string _lastJobLog = string.Empty;
    private string _lastLogEntry = string.Empty;
    private bool _backendReady;
    private string _currentPlatform = "instagram";
    private string _currentLang = "de";

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
        ApplySettings();
        AnimateTabIndicator(TabInsta);
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
            _launcher.Start();
            _backendReady = await _backend.WaitForHealthAsync(
                CancellationToken.None, SettingsService.Current.BackendTimeoutSeconds);
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
        HelpPopup.IsOpen = !HelpPopup.IsOpen;
    }

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
        if (string.IsNullOrEmpty(url)) { Warn(L("ErrNoUrl")); return; }
        if (!_backendReady) { Warn(L("ErrNoBackend")); return; }

        BtnFetch.IsEnabled = false;
        SetStatus(L("StatusLoading"), true);

        try
        {
            _meta = await _backend.FetchMetadataAsync(url);
            if (_meta is null) { Warn(L("ErrNoMeta")); return; }

            TxtMetaTitle.Text = _meta.Title;
            TxtMetaUploader.Text = $"👤 {_meta.Uploader}";
            TxtMetaDuration.Text = $"⏱ {TimeSpan.FromSeconds(_meta.Duration):mm\\:ss}";
            TagsList.ItemsSource = _meta.Tags.Take(8).Select(t => $"#{t}").ToList();

            TxtTitle.Text = _meta.Title;
            TxtDescription.Text = _meta.Description;

            AnimatePanel(BorderMeta, true);
            AnimatePanel(BorderQuickActions, true);
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

        BtnStart.IsEnabled = false;
        BtnFetch.IsEnabled = false;

        _lastJobLog = string.Empty;
        _lastLogEntry = string.Empty;
        _logViewer?.Close();

        _cts = new CancellationTokenSource();
        _progressWin = new ProgressWindow(_cts) { Owner = this };
        _progressWin.Closed += (_, _) =>
        {
            _lastJobLog = _progressWin?.LogContent ?? string.Empty;
            _progressWin = null;
            BtnMainLog.IsEnabled = !string.IsNullOrEmpty(_lastJobLog);
            BtnMainLog.Content = "\u25be Log";
            if (!_cts.IsCancellationRequested) _cts.Cancel();
        };
        BtnMainLog.IsEnabled = true;
        _progressWin.Show();
        _progressWin.UpdateProgress(0, "Starte…", "Verbinde mit Backend…");
        _progressWin.AppendLog("Verbinde mit Backend…");

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

            _progressWin?.AppendLog($"Job gestartet: {jobId}");

            await foreach (var status in _backend.StreamJobAsync(jobId, _cts.Token))
            {
                var detail = status.Status == "completed" ? "Abgeschlossen."
                           : status.Status == "error"     ? $"Fehler: {status.Error}"
                           : "";
                _progressWin?.UpdateProgress(status.Progress, status.Message, detail);
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
                    _progressWin?.MarkDone(true);
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
        catch (OperationCanceledException) { SetStatus(L("StatusCancelled"), false); }
        catch (Exception ex)
        {
            SetStatus($"{L("StatusErrPrefix")} {ex.Message}", false);
            _progressWin?.MarkDone(false);
        }
        finally
        {
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
        var win = new SettingsWindow { Owner = this };
        if (win.ShowDialog() == true)
            ApplySettings();
    }

    private void ApplySettings()
    {
        var s = SettingsService.Current;
        if (s.Language != _currentLang) SetLanguage(s.Language);
        SelectComboByTag(CmbPrivacy, s.DefaultPrivacy);
        ChkFingerprint.IsChecked = s.DefaultFingerprintEnabled;
        SelectComboByTag(CmbFingerprintMethod, s.DefaultFingerprintMethod);
        if (!string.IsNullOrEmpty(s.DefaultOutputDir))
            TxtOutputDir.Text = s.DefaultOutputDir;
        ChkFingerprintDl.IsChecked = s.DefaultFingerprintDlEnabled;
        SelectComboByTag(CmbFingerprintMethodDl, s.DefaultFingerprintDlMethod);
    }

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
            if (item.Tag?.ToString() == tag) { combo.SelectedItem = item; return; }
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

    private void CopyDesc_Click(object s, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(TxtDescription.Text))
            Clipboard.SetText(TxtDescription.Text);
    }

    private void CopyTags_Click(object s, RoutedEventArgs e)
    {
        if (_meta?.Tags is { Count: > 0 })
            Clipboard.SetText(string.Join(" ", _meta.Tags.Select(t => $"#{t}")));
    }

    private void ClearForm_Click(object s, RoutedEventArgs e)
    {
        TxtUrl.Text = string.Empty;
        _meta = null;
        AnimatePanel(BorderMeta, false);
        AnimatePanel(BorderQuickActions, false);
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

    private void MainLog_Click(object s, RoutedEventArgs e)
    {
        if (_progressWin != null)
        {
            _progressWin.ToggleLog();
            BtnMainLog.Content = _progressWin.IsLogOpen ? "\u25b4 Log" : "\u25be Log";
            return;
        }

        if (string.IsNullOrEmpty(_lastJobLog)) return;

        if (_logViewer != null)
        {
            _logViewer.Activate();
            return;
        }

        _logViewer = new LogViewerWindow(_lastJobLog) { Owner = this };
        _logViewer.Closed += (_, _) => _logViewer = null;
        _logViewer.Show();
    }

    }
