using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ReelsConverterUI;

public partial class ProgressWindow : Window
{
    private readonly CancellationTokenSource _cts;
    private bool _done;

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
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            timer.Tick += (_, _) => { timer.Stop(); Close(); };
            timer.Start();
        }
    }

    private void Cancel_Click(object s, RoutedEventArgs e)
    {
        if (!_done) _cts.Cancel();
        Close();
    }
}
