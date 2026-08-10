using ReelsConverterUI.Animations;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ReelsConverterUI;

public partial class LogViewerWindow : Window
{
    private readonly Rect _originRect;
    private bool _isAnimatingClose;

    public LogViewerWindow(string logContent, Rect originRect)
    {
        InitializeComponent();
        _originRect = originRect;
        TxtLog.Text = logContent;
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (Services.SettingsService.Current.BlurLogViewer)
        {
            Services.WindowBlurHelper.EnableBlurWithFade(this, RootBorder);
        }
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
        Services.SettingsService.SettingsChanged += (_, _) => Services.SettingsService.ApplyResizeGripVisibility(this);
        Services.SettingsService.ApplyResizeGripVisibility(this);
        Services.SettingsService.ApplyWindowSize(this);
        if (Services.SettingsService.Current.BlurLogViewer)
        {
            Services.WindowBlurHelper.EnableBlurWithFade(this, RootBorder);
            Services.WindowBlurHelper.ApplyRoundedRegion(this);
        }
        FluidMotion.MorphOpen(RootBorder, WinScale, WinTranslate, _originRect, this);
        TxtLog.ScrollToEnd();
    }

    private void TitleBar_Drag(object s, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Copy_Click(object s, RoutedEventArgs e)
        => Clipboard.SetText(TxtLog.Text);

    private void Close_Click(object s, RoutedEventArgs e) => CloseWithAnimation();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isAnimatingClose) { e.Cancel = true; CloseWithAnimation(); }
        base.OnClosing(e);
    }

    private void CloseWithAnimation()
    {
        if (_isAnimatingClose) return;
        _isAnimatingClose = true;
        FluidMotion.MorphClose(RootBorder, WinScale, WinTranslate, _originRect, this,
            () => Close());
    }
}
