using System.Windows;
using System.Windows.Input;

namespace ReelsConverterUI;

public partial class LogViewerWindow : Window
{
    public LogViewerWindow(string logContent)
    {
        InitializeComponent();
        TxtLog.Text = logContent;
        Loaded += (_, _) => TxtLog.ScrollToEnd();
    }

    private void TitleBar_Drag(object s, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Copy_Click(object s, RoutedEventArgs e)
        => Clipboard.SetText(TxtLog.Text);

    private void Close_Click(object s, RoutedEventArgs e)
        => Close();
}
