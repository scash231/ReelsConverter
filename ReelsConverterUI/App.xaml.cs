using System.Windows;
using System.Windows.Threading;
using ReelsConverterUI.Services;

namespace ReelsConverterUI;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnUnhandled;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        ThemeService.Apply(ThemeService.Current);
        base.OnStartup(e);
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
