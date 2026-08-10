using System.IO;
using System.Text.Json;
using ReelsConverterUI.Models;

namespace ReelsConverterUI.Services;

public static class SettingsService
{
    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ReelsConverter",
        "settings.json");

    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

    public static AppSettings Current { get; private set; } = Load();

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<AppSettings>(json, _opts) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    public static event EventHandler? SettingsChanged;

    public static void Save(AppSettings settings)
    {
        Current = settings;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, _opts));
        ApplyScrollbarVisibility();
        SettingsChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void ApplyResizeGripVisibility(System.Windows.Window window)
    {
        if (window == null) return;
        var grip = window.FindName("WindowCornerGrip") as System.Windows.FrameworkElement;
        if (grip != null)
        {
            if (!Current.ShowWindowResizerGrip)
            {
                grip.Visibility = System.Windows.Visibility.Collapsed;
            }
            else
            {
                grip.Visibility = System.Windows.Visibility.Visible;
                grip.Opacity = Current.ResizerGripOnlyOnHover ? (grip.IsMouseOver ? 0.85 : 0.0) : 0.75;
            }
        }
    }

    public static void HandleGripHover(object sender, bool isHovered)
    {
        if (sender is System.Windows.FrameworkElement grip && Current.ShowWindowResizerGrip)
        {
            if (Current.ResizerGripOnlyOnHover)
            {
                grip.Opacity = isHovered ? 0.85 : 0.0;
            }
            else
            {
                grip.Opacity = 0.75;
            }
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    public static void StartWindowResizeBottomRight(System.Windows.Window window)
    {
        if (window == null) return;
        var helper = new System.Windows.Interop.WindowInteropHelper(window);
        if (helper.Handle != IntPtr.Zero)
        {
            SendMessage(helper.Handle, 0xA1, (IntPtr)17, IntPtr.Zero);
        }
    }

    public static void ApplyScrollbarVisibility()
    {
        var s = Current;
        var width = s.HideScrollbars ? 0.0 : 6.0;
        var height = s.HideScrollbars ? 0.0 : 6.0;

        if (System.Windows.Application.Current != null)
        {
            var res = System.Windows.Application.Current.Resources;
            res["GlobalScrollBarWidth"] = width;
            res["GlobalScrollBarHeight"] = height;
        }
    }

    public static void ApplyWindowSize(System.Windows.Window window)
    {
        if (window == null) return;
        var s = Current;

        if (window is MainWindow)
        {
            if (s.MainWindowWidth >= 500) window.Width = s.MainWindowWidth;
            if (s.MainWindowHeight >= 400) window.Height = s.MainWindowHeight;
        }
        else if (window is EditorWindow)
        {
            if (s.EditorWindowWidth >= 500) window.Width = s.EditorWindowWidth;
            if (s.EditorWindowHeight >= 400) window.Height = s.EditorWindowHeight;
        }
        else if (window is SettingsWindow)
        {
            if (s.SettingsWindowWidth >= 500) window.Width = s.SettingsWindowWidth;
            if (s.SettingsWindowHeight >= 400) window.Height = s.SettingsWindowHeight;
        }
        else if (window is DesignerWindow)
        {
            if (s.DesignerWindowWidth >= 500) window.Width = s.DesignerWindowWidth;
            if (s.DesignerWindowHeight >= 400) window.Height = s.DesignerWindowHeight;
        }
        else if (window is DescriptionEditorWindow)
        {
            if (s.DescEditorWindowWidth >= 400) window.Width = s.DescEditorWindowWidth;
            if (s.DescEditorWindowHeight >= 300) window.Height = s.DescEditorWindowHeight;
        }
        else if (window is DevConsoleWindow)
        {
            if (s.DevConsoleWindowWidth >= 350) window.Width = s.DevConsoleWindowWidth;
            if (s.DevConsoleWindowHeight >= 200) window.Height = s.DevConsoleWindowHeight;
        }
        else if (window is LogViewerWindow)
        {
            if (s.LogViewerWindowWidth >= 350) window.Width = s.LogViewerWindowWidth;
            if (s.LogViewerWindowHeight >= 200) window.Height = s.LogViewerWindowHeight;
        }
    }
}
