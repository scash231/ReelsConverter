using ReelsConverterUI.Animations;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ReelsConverterUI;

public partial class ProgressWindow : Window
{
    private readonly CancellationTokenSource _cts;
    private readonly Rect _originRect;
    private bool _done;
    private bool _isAnimatingClose;
    private DispatcherTimer? _countdownTimer;
    private int _countdownRemaining;
    private string? _folderPath;
    private DateTime? _jobStartTime;
    private string _lastPhaseKey = string.Empty;
    private bool _isPhaseAnimating;
    private (int pct, string message, string detail, int? eta, string? speed) _pending;
    private double? _smoothedEta;
    private const double EtaAlpha = 0.15;
    private DateTime _lastEtaUpdateTime = DateTime.MinValue;

    private bool _hasNotifiedCompletion;
    public bool IsLogOpen => TxtConsole.Visibility == Visibility.Visible;
    public string LogContent => TxtConsole.Text;

    public event EventHandler? OnHiddenInBackground;

    public ProgressWindow(CancellationTokenSource cts, Rect originRect)
    {
        InitializeComponent();
        _cts = cts;
        _originRect = originRect;
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        Services.WindowBlurHelper.EnableBlurWithFade(this, RootBorder);
        Services.WindowBlurHelper.ApplyRoundedRegion(this);
    }

    private void WindowCornerGrip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            Services.SettingsService.StartWindowResizeBottomRight(this);
    }

    private void WindowCornerGrip_MouseEnter(object sender, MouseEventArgs e)
        => Services.SettingsService.HandleGripHover(sender, true);

    private void WindowCornerGrip_MouseLeave(object sender, MouseEventArgs e)
        => Services.SettingsService.HandleGripHover(sender, false);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Retrieve the theme's deep background brush and apply it with transparency for a dynamic glass tint
        var deepBrush = TryFindResource("BgDeep") as SolidColorBrush;
        if (deepBrush != null)
        {
            var color = deepBrush.Color;
            RootBorder.Background = new SolidColorBrush(Color.FromArgb(158, color.R, color.G, color.B));
        }

        Services.WindowBlurHelper.EnableBlurWithFade(this, RootBorder);
        Services.SettingsService.SettingsChanged += (_, _) => Services.SettingsService.ApplyResizeGripVisibility(this);
        Services.SettingsService.ApplyResizeGripVisibility(this);
        FluidMotion.MorphOpen(RootBorder, WinScale, WinTranslate, _originRect, this);
    }

    private void TitleBar_Drag(object s, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    public void UpdateProgress(int pct, string message, string detail, int? eta = null, string? speed = null)
    {
        if (_done) return;

        var phaseKey = GetPhaseKey(message);
        var isPhaseChange = !_isPhaseAnimating
                            && _lastPhaseKey.Length > 0
                            && phaseKey.Length > 0
                            && phaseKey != _lastPhaseKey;

        if (phaseKey.Length > 0 && phaseKey != _lastPhaseKey)
        {
            _lastPhaseKey = phaseKey;
            _smoothedEta = null;
            _jobStartTime = DateTime.UtcNow;
        }

        if (_jobStartTime == null && pct > 0)
            _jobStartTime = DateTime.UtcNow;

        int? effectiveEta = eta is > 0 ? eta : null;
        if (effectiveEta == null && _jobStartTime.HasValue && pct >= 3 && pct < 100)
        {
            var elapsed = (DateTime.UtcNow - _jobStartTime.Value).TotalSeconds;
            if (elapsed > 0.8)
            {
                effectiveEta = (int)Math.Max(1, Math.Round(elapsed * (100 - pct) / pct));
            }
        }

        if (effectiveEta is > 0)
        {
            _smoothedEta = _smoothedEta == null
                ? effectiveEta.Value
                : EtaAlpha * effectiveEta.Value + (1.0 - EtaAlpha) * _smoothedEta.Value;
            effectiveEta = (int)Math.Max(1, Math.Round(_smoothedEta.Value));
        }

        _pending = (pct, message, detail, effectiveEta, speed);

        if (isPhaseChange)
            AnimatePhaseTransition();
        else if (!_isPhaseAnimating)
            ApplyProgressValues(pct, message, detail, effectiveEta, speed);
    }

    private static string GetPhaseKey(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return string.Empty;
        return message.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]
                      .TrimEnd('.', '…', ',', ':')
                      .ToLowerInvariant();
    }

    private void ApplyProgressValues(int pct, string message, string detail, int? effectiveEta, string? speed = null)
    {
        TxtProgressMsg.Text = message;
        TxtProgressPct.Text = $"{pct}%";
        TxtProgressDetail.Text = detail;

        var etaStr = string.Empty;
        if (effectiveEta is > 0 && pct < 100)
        {
            var ts = TimeSpan.FromSeconds(effectiveEta.Value);
            etaStr = ts.TotalHours >= 1
                ? $"ETA {ts:hh\\:mm\\:ss}"
                : $"ETA {ts:mm\\:ss}";
        }

        if (!string.IsNullOrWhiteSpace(speed) && !string.IsNullOrWhiteSpace(etaStr))
        {
            TxtEta.Text = $"{speed}  •  {etaStr}";
        }
        else if (!string.IsNullOrWhiteSpace(speed))
        {
            TxtEta.Text = speed;
        }
        else if (!string.IsNullOrWhiteSpace(etaStr))
        {
            TxtEta.Text = etaStr;
        }
        else
        {
            TxtEta.Text = string.Empty;
        }

        var totalWidth = ProgressTrack.ActualWidth > 0 ? ProgressTrack.ActualWidth : 388;
        FluidMotion.AnimateProgressWidth(ProgressFill, totalWidth * pct / 100.0);
    }

    private void AnimatePhaseTransition()
    {
        _isPhaseAnimating = true;
        FrameworkElement[] rows = [RowMsgPct, ProgressTrack, RowDetail];
        FluidMotion.PhaseOut(rows, () =>
        {
            ApplyProgressValues(_pending.pct, _pending.message, _pending.detail, _pending.eta, _pending.speed);
            AnimatePhaseIn(rows);
        });
    }

    private void AnimatePhaseIn(FrameworkElement[] rows)
    {
        FluidMotion.PhaseIn(rows, () =>
        {
            _isPhaseAnimating = false;
            ApplyProgressValues(_pending.pct, _pending.message, _pending.detail, _pending.eta, _pending.speed);
        });
    }

    public void MarkDone(bool success, string? folderPath = null)
    {
        _done = true;
        _folderPath = folderPath;
        TxtEta.Text = string.Empty;
        BtnCancel.Content = "Schlie\u00dfen";

        if (success)
            ApplyProgressValues(100, _pending.message, "", null);

        if (!string.IsNullOrEmpty(_folderPath))
            BtnOpenFolder.Visibility = Visibility.Visible;

        if (success)
        {
            _countdownRemaining = 4;
            TxtCountdown.Text = $"Schließt in {_countdownRemaining} s…";
            TxtCountdown.Visibility = Visibility.Visible;
            StartCountdown();
        }

        if (Services.SettingsService.Current.NotifyOnComplete && !_hasNotifiedCompletion)
        {
            _hasNotifiedCompletion = true;
            try
            {
                bool playSound = Services.SettingsService.Current.EnableNotificationSound;
                if (success)
                {
                    if (playSound) try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
                    NotificationWindow.Show("Konvertierung erfolgreich abgeschlossen!", Owner ?? this, NotificationType.Info);
                }
                else
                {
                    if (playSound) try { System.Media.SystemSounds.Hand.Play(); } catch { }
                    NotificationWindow.Show("Konvertierung fehlgeschlagen!", Owner ?? this, NotificationType.Error);
                }
            }
            catch { }
        }
    }

    private void StartCountdown()
    {
        _countdownTimer?.Stop();
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) =>
        {
            _countdownRemaining--;
            if (_countdownRemaining <= 0)
            {
                _countdownTimer.Stop();
                CloseWithAnimation();
            }
            else
            {
                TxtCountdown.Text = $"Schlie\u00dft in {_countdownRemaining} s\u2026";
            }
        };
        _countdownTimer.Start();
    }

    private void Cancel_Click(object s, RoutedEventArgs e)
    {
        if (!_done) _cts.Cancel();
        CloseWithAnimation();
    }

    private void OpenFolder_Click(object s, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_folderPath))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _folderPath,
                    UseShellExecute = true,
                });
            }
            catch { /* ignore if folder no longer exists */ }
        }
    }

    public void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        TxtConsole.AppendText(TxtConsole.Text.Length == 0 ? line : Environment.NewLine + line);
        TxtConsole.ScrollToEnd();
    }

    public void ToggleLog()
    {
        var opening = TxtConsole.Visibility != Visibility.Visible;
        BtnToggleLog.Content = opening ? "\u25b4 Log" : "\u25be Log";

        if (opening)
        {
            FluidMotion.ExpandElement(TxtConsole, 140);
            _countdownTimer?.Stop();
        }
        else
        {
            var fromHeight = Math.Min(TxtConsole.ActualHeight, 140);
            FluidMotion.CollapseElement(TxtConsole, fromHeight, () =>
            {
                if (_done && _countdownRemaining > 0)
                    StartCountdown();
            });
        }
    }

    public void EnsureLogOpen()
    {
        if (TxtConsole.Visibility != Visibility.Visible)
        {
            ToggleLog();
        }
    }

    private void ToggleLog_Click(object s, RoutedEventArgs e) => ToggleLog();

    public void ShowWithAnimation()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        _isAnimatingClose = false;
        try { Services.WindowBlurHelper.EnableBlurWithFade(this, RootBorder); } catch { }
        FluidMotion.MorphOpen(RootBorder, WinScale, WinTranslate, _originRect, this);
    }

    private void CloseWindow_Click(object s, RoutedEventArgs e)
    {
        CloseWithAnimation(realClose: false);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isAnimatingClose) 
        { 
            e.Cancel = true; 
            CloseWithAnimation(realClose: _done); 
        }
        base.OnClosing(e);
    }

    private void CloseWithAnimation() => CloseWithAnimation(realClose: true);

    private void CloseWithAnimation(bool realClose)
    {
        if (_isAnimatingClose) return;
        _isAnimatingClose = true;
        _countdownRemaining = 0;
        _countdownTimer?.Stop();
        FluidMotion.MorphClose(RootBorder, WinScale, WinTranslate, _originRect, this,
            () =>
            {
                if (realClose)
                {
                    Close();
                }
                else
                {
                    _isAnimatingClose = false;
                    Hide();
                    OnHiddenInBackground?.Invoke(this, EventArgs.Empty);
                }
            });
    }
}
