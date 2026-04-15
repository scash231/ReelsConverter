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
    private (int pct, string message, string detail, int? eta) _pending;
    private double? _smoothedEta;
    private const double EtaAlpha = 0.10;
    private DateTime _lastEtaUpdateTime = DateTime.MinValue;

    public bool IsLogOpen => TxtConsole.Visibility == Visibility.Visible;
    public string LogContent => TxtConsole.Text;

    public ProgressWindow(CancellationTokenSource cts, Rect originRect)
    {
        InitializeComponent();
        _cts = cts;
        _originRect = originRect;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FluidMotion.MorphOpen(RootBorder, WinScale, WinTranslate, _originRect, this);
    }

    private void TitleBar_Drag(object s, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    public void UpdateProgress(int pct, string message, string detail, int? eta = null)
    {
        if (_done) return;

        if (_jobStartTime == null && pct > 0)
            _jobStartTime = DateTime.UtcNow;

        int? effectiveEta = eta is > 0 ? eta : null;
        if (effectiveEta == null && _jobStartTime.HasValue && pct >= 5 && pct < 100)
        {
            var elapsed = (DateTime.UtcNow - _jobStartTime.Value).TotalSeconds;
            effectiveEta = (int)(elapsed * (100 - pct) / pct);
        }

        if (effectiveEta is > 0)
        {
            _smoothedEta = _smoothedEta == null
                ? effectiveEta.Value
                : EtaAlpha * effectiveEta.Value + (1 - EtaAlpha) * _smoothedEta.Value;
            effectiveEta = (int)Math.Round(_smoothedEta.Value);
        }

        var phaseKey = GetPhaseKey(message);
        var isPhaseChange = !_isPhaseAnimating
                            && _lastPhaseKey.Length > 0
                            && phaseKey.Length > 0
                            && phaseKey != _lastPhaseKey;
        if (phaseKey.Length > 0) _lastPhaseKey = phaseKey;

        _pending = (pct, message, detail, effectiveEta);

        if (isPhaseChange)
            AnimatePhaseTransition();
        else if (!_isPhaseAnimating)
            ApplyProgressValues(pct, message, detail, effectiveEta);
    }

    private static string GetPhaseKey(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return string.Empty;
        return message.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]
                      .TrimEnd('.', '…', ',', ':')
                      .ToLowerInvariant();
    }

    private void ApplyProgressValues(int pct, string message, string detail, int? effectiveEta)
    {
        TxtProgressMsg.Text = message;
        TxtProgressPct.Text = $"{pct}%";
        TxtProgressDetail.Text = detail;

        if (effectiveEta is > 0)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastEtaUpdateTime).TotalSeconds >= 1.0)
            {
                var ts = TimeSpan.FromSeconds(effectiveEta.Value);
                TxtEta.Text = ts.TotalHours >= 1
                    ? $"ETA {ts:hh\\:mm\\:ss}"
                    : $"ETA {ts:mm\\:ss}";
                _lastEtaUpdateTime = now;
            }
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
            ApplyProgressValues(_pending.pct, _pending.message, _pending.detail, _pending.eta);
            AnimatePhaseIn(rows);
        });
    }

    private void AnimatePhaseIn(FrameworkElement[] rows)
    {
        FluidMotion.PhaseIn(rows, () =>
        {
            _isPhaseAnimating = false;
            ApplyProgressValues(_pending.pct, _pending.message, _pending.detail, _pending.eta);
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
            TxtCountdown.Text = $"Schlie\u00dft in {_countdownRemaining} s\u2026";
            TxtCountdown.Visibility = Visibility.Visible;
            StartCountdown();
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

    private void ToggleLog_Click(object s, RoutedEventArgs e) => ToggleLog();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isAnimatingClose) { e.Cancel = true; CloseWithAnimation(); }
        base.OnClosing(e);
    }

    private void CloseWithAnimation()
    {
        if (_isAnimatingClose) return;
        _isAnimatingClose = true;
        _countdownTimer?.Stop();
        FluidMotion.MorphClose(RootBorder, WinScale, WinTranslate, _originRect, this,
            () => Close());
    }
}
