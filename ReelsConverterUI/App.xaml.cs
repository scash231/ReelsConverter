using System.Windows;
using System.Windows.Threading;

namespace ReelsConverterUI;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnUnhandled;
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var msg = e.Exception.ToString();
        if (e.Exception.InnerException is not null)
            msg = e.Exception.InnerException.ToString() + "\n\n" + msg;
        MessageBox.Show(msg, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
