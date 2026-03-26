using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ReelsConverterUI;

public partial class ProgressWindow : Window
{
    private readonly CancellationTokenSource _cts;
    private bool _done;
    private DispatcherTimer? _countdownTimer;
    private int _countdownRemaining;

    public bool IsLogOpen => TxtConsole.Visibility == Visibility.Visible;
    public string LogContent => TxtConsole.Text;

    public ProgressWindow(CancellationTokenSource cts)
    {
        InitializeComponent();
        _cts = cts;
    }

    private void TitleBar_Drag(object s, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    public void UpdateProgress(int pct, string message, string detail)
    {
        if (_done) return;
        TxtProgressMsg.Text = message;
        TxtProgressPct.Text = $"{pct}%";
        TxtProgressDetail.Text = detail;

        var totalWidth = ProgressTrack.ActualWidth > 0 ? ProgressTrack.ActualWidth : 388;
        var anim = new DoubleAnimation(totalWidth * pct / 100.0, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ProgressFill.BeginAnimation(WidthProperty, anim);
    }

    public void MarkDone(bool success)
    {
        _done = true;
        BtnCancel.Content = "Schließen";

        if (success)
        {
            _countdownRemaining = 3;
            TxtCountdown.Text = $"Schließt in {_countdownRemaining} s\u2026";
            TxtCountdown.Visibility = System.Windows.Visibility.Visible;
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
                Close();
            }
            else
            {
                TxtCountdown.Text = $"Schließt in {_countdownRemaining} s\u2026";
            }
        };
        _countdownTimer.Start();
    }

    private void Cancel_Click(object s, RoutedEventArgs e)
    {
        if (!_done) _cts.Cancel();
        Close();
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
        TxtConsole.Visibility = opening ? Visibility.Visible : Visibility.Collapsed;
        BtnToggleLog.Content = opening ? "\u25b4 Log" : "\u25be Log";

        if (opening)
        {
            _countdownTimer?.Stop();
        }
        else if (_done && _countdownRemaining > 0)
        {
            StartCountdown();
        }
    }

    private void ToggleLog_Click(object s, RoutedEventArgs e) => ToggleLog();
}
