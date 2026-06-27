using ReelsConverterUI.Animations;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ReelsConverterUI;

public partial class DevConsoleWindow : Window
{
    private readonly Rect _originRect;
    private bool _isAnimatingClose;
    private readonly List<string> _cmdHistory = new();
    private int _historyIndex = -1;

    public bool ReattachRequested { get; private set; }

    /// <summary>Fired when the user enters a command. The handler should process it.</summary>
    public event Action<string>? CommandEntered;

    public DevConsoleWindow(string existingLog, Rect originRect)
    {
        InitializeComponent();
        _originRect = originRect;
        TxtConsole.Text = existingLog;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Services.SettingsService.Current.BlurDevConsole)
        {
            Services.WindowBlurHelper.EnableBlurWithFade(this, RootBorder);
        }
        FluidMotion.MorphOpen(RootBorder, WinScale, WinTranslate, _originRect, this);
        TxtConsole.ScrollToEnd();
        TxtInput.Focus();
    }

    private void TitleBar_Drag(object s, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    public void AppendLog(string line)
    {
        TxtConsole.AppendText(TxtConsole.Text.Length == 0 ? line : Environment.NewLine + line);
        TxtConsole.ScrollToEnd();
    }

    public void ClearConsole()
        => TxtConsole.Clear();

    private void Clear_Click(object s, RoutedEventArgs e)
        => TxtConsole.Clear();

    private void Help_Click(object s, RoutedEventArgs e)
    {
        ConsoleHelpPopup.PlacementTarget = (UIElement)s;
        if (!ConsoleHelpPopup.IsOpen)
        {
            ConsoleHelpPopup.IsOpen = true;
            AnimatePopupIn(HelpPopupBorder);
        }
        else
        {
            AnimatePopupOut(HelpPopupBorder, () => ConsoleHelpPopup.IsOpen = false);
        }
    }

    private static void AnimatePopupIn(System.Windows.Controls.Border border)
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

    private static void AnimatePopupOut(System.Windows.Controls.Border border, Action onDone)
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

    private void Minimize_Click(object s, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Reattach_Click(object s, RoutedEventArgs e)
    {
        ReattachRequested = true;
        CloseWithAnimation();
    }

    private void Close_Click(object s, RoutedEventArgs e) => CloseWithAnimation();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isAnimatingClose) { e.Cancel = true; CloseWithAnimation(); }
        base.OnClosing(e);
    }

    private void TxtInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var cmd = TxtInput.Text.Trim();
            if (string.IsNullOrEmpty(cmd)) return;
            _cmdHistory.Add(cmd);
            _historyIndex = _cmdHistory.Count;
            AppendLog($"> {cmd}");
            CommandEntered?.Invoke(cmd);
            TxtInput.Clear();
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (_cmdHistory.Count > 0 && _historyIndex > 0)
            {
                _historyIndex--;
                TxtInput.Text = _cmdHistory[_historyIndex];
                TxtInput.CaretIndex = TxtInput.Text.Length;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            if (_historyIndex < _cmdHistory.Count - 1)
            {
                _historyIndex++;
                TxtInput.Text = _cmdHistory[_historyIndex];
                TxtInput.CaretIndex = TxtInput.Text.Length;
            }
            else
            {
                _historyIndex = _cmdHistory.Count;
                TxtInput.Clear();
            }
            e.Handled = true;
        }
    }

    private void CloseWithAnimation()
    {
        if (_isAnimatingClose) return;
        _isAnimatingClose = true;
        FluidMotion.MorphClose(RootBorder, WinScale, WinTranslate, _originRect, this,
            () => Close());
    }
}
