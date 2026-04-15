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
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
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
