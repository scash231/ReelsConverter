using Microsoft.Win32;
using ReelsConverterUI.Animations;
using ReelsConverterUI.Models;
using ReelsConverterUI.Services;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ReelsConverterUI;

public partial class SettingsWindow : Window
{
    private string _lang = "de";
    private readonly Rect _originRect;
    private bool _isAnimatingClose;
    private bool? _pendingResult;
    private StackPanel? _activePanel;

    public SettingsWindow(Rect originRect)
    {
        InitializeComponent();
        _originRect = originRect;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FluidMotion.MorphOpen(RootBorder, WindowScale, WindowTranslate, _originRect, this);

        _activePanel = PanelGeneral;
        AnimateTabContent(PanelGeneral);

        var s = SettingsService.Current;
        _lang = s.Language;
        UpdateLangButtons();

        // General
        ChkAlwaysOnTop.IsChecked = s.AlwaysOnTop;
        ChkAutoPaste.IsChecked = s.AutoPasteOnFocus;
        ChkAutoFetch.IsChecked = s.AutoFetchMetadata;
        ChkNotifyComplete.IsChecked = s.NotifyOnComplete;

        // Upload
        SelectComboByTag(CmbDefaultPrivacy, s.DefaultPrivacy);
        ChkAutoShorts.IsChecked = s.AutoAddShortsHashtag;
        ChkDefaultFp.IsChecked = s.DefaultFingerprintEnabled;
        SelectComboByTag(CmbDefaultFpMethod, s.DefaultFingerprintMethod);

        // Download
        TxtDefaultOutputDir.Text = s.DefaultOutputDir;
        SelectComboByTag(CmbVideoQuality, s.DefaultVideoQuality);
        ChkDefaultFpDl.IsChecked = s.DefaultFingerprintDlEnabled;
        SelectComboByTag(CmbDefaultFpMethodDl, s.DefaultFingerprintDlMethod);

        // Advanced
        ChkUseGpu.IsChecked = s.UseGpu;
        SelectComboByTag(CmbMaxJobs, s.MaxConcurrentJobs.ToString());
        TxtBackendUrl.Text = s.BackendUrl;
        TxtBackendTimeout.Text = s.BackendTimeoutSeconds.ToString();
        ChkDevConsole.IsChecked = s.DevConsoleEnabled;
    }

    // ═══ Title Bar ═══
    private void TitleBar_Drag(object s, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    // ═══ Language ═══
    private void SLangDE_Click(object s, RoutedEventArgs e) { _lang = "de"; UpdateLangButtons(); }
    private void SLangEN_Click(object s, RoutedEventArgs e) { _lang = "en"; UpdateLangButtons(); }

    private void UpdateLangButtons()
    {
        SLangDE.Foreground = _lang == "de"
            ? (Brush)Application.Current.Resources["Accent"]
            : (Brush)Application.Current.Resources["TextSec"];
        SLangEN.Foreground = _lang == "en"
            ? (Brush)Application.Current.Resources["Accent"]
            : (Brush)Application.Current.Resources["TextSec"];
    }

    // ═══ Tab Switching ═══
    private void Tab_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;

        var target = sender == TabGeneral   ? PanelGeneral
                   : sender == TabUpload    ? PanelUpload
                   : sender == TabDownload  ? PanelDownload
                   : sender == TabAdvanced  ? PanelAdvanced
                   : null;

        if (target == null || target == _activePanel) return;
        SwitchTab(target);
    }

    private void SwitchTab(StackPanel target)
    {
        var old = _activePanel;
        _activePanel = target;

        if (old != null)
        {
            var ease = AppleSpringEase.Snappy;
            var dur = TimeSpan.FromMilliseconds(150);
            var fadeOut = new DoubleAnimation(1, 0, dur) { EasingFunction = ease };
            fadeOut.Completed += (_, _) =>
            {
                old.Visibility = Visibility.Collapsed;
                target.Visibility = Visibility.Visible;
                AnimateTabContent(target);
            };
            old.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
        else
        {
            target.Visibility = Visibility.Visible;
            AnimateTabContent(target);
        }
    }

    private static void AnimateTabContent(StackPanel panel)
    {
        panel.Opacity = 0;
        panel.RenderTransformOrigin = new Point(0.5, 0.0);
        var group = new TransformGroup();
        var st = new ScaleTransform(0.97, 0.97);
        var tt = new TranslateTransform(0, 8);
        group.Children.Add(st);
        group.Children.Add(tt);
        panel.RenderTransform = group;

        var spring = AppleSpringEase.Interactive;
        var smooth = AppleSpringEase.Gentle;
        var springDur = TimeSpan.FromMilliseconds(420);

        panel.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
            { EasingFunction = smooth });
        tt.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(8, 0, springDur) { EasingFunction = spring });
        st.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.97, 1, springDur) { EasingFunction = spring });
        st.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.97, 1, springDur) { EasingFunction = spring });

        // Stagger children
        int idx = 0;
        foreach (UIElement child in panel.Children)
        {
            if (child is not FrameworkElement fe) continue;
            fe.Opacity = 0;
            fe.RenderTransformOrigin = new Point(0.5, 0.0);
            var cGroup = new TransformGroup();
            var cTt = new TranslateTransform(0, 12 + idx * 2);
            cGroup.Children.Add(new ScaleTransform(1, 1));
            cGroup.Children.Add(cTt);
            fe.RenderTransform = cGroup;

            var delay = TimeSpan.FromMilliseconds(40 + idx * 35);
            fe.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
                { BeginTime = delay, EasingFunction = smooth });
            cTt.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(12 + idx * 2, 0, TimeSpan.FromMilliseconds(380))
                { BeginTime = delay, EasingFunction = spring });
            idx++;
        }
    }

    // ═══ Browse ═══
    private void BrowseOutput_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Default save location" };
        if (dlg.ShowDialog() == true)
            TxtDefaultOutputDir.Text = dlg.FolderName;
    }

    // ═══ Reset ═══
    private void ResetAll_Click(object s, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            L("SettingsResetConfirm"), "ReelsConverter",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        var defaults = new AppSettings();
        SettingsService.Save(defaults);
        CloseWithAnimation(true);
    }

    // ═══ Save / Cancel ═══
    private void Save_Click(object s, RoutedEventArgs e)
    {
        var settings = new AppSettings
        {
            // General
            Language                     = _lang,
            AlwaysOnTop                  = ChkAlwaysOnTop.IsChecked == true,
            AutoPasteOnFocus             = ChkAutoPaste.IsChecked == true,
            AutoFetchMetadata            = ChkAutoFetch.IsChecked == true,
            NotifyOnComplete             = ChkNotifyComplete.IsChecked == true,

            // Upload
            DefaultPrivacy               = (CmbDefaultPrivacy.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "public",
            AutoAddShortsHashtag         = ChkAutoShorts.IsChecked == true,
            DefaultFingerprintEnabled    = ChkDefaultFp.IsChecked == true,
            DefaultFingerprintMethod     = (CmbDefaultFpMethod.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "standard",

            // Download
            DefaultOutputDir             = TxtDefaultOutputDir.Text.Trim(),
            DefaultVideoQuality          = (CmbVideoQuality.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "best",
            DefaultFingerprintDlEnabled  = ChkDefaultFpDl.IsChecked == true,
            DefaultFingerprintDlMethod   = (CmbDefaultFpMethodDl.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "standard",

            // Advanced
            UseGpu                       = ChkUseGpu.IsChecked == true,
            MaxConcurrentJobs            = int.TryParse((CmbMaxJobs.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var mj) ? mj : 1,
            BackendUrl                   = TxtBackendUrl.Text.Trim(),
            BackendTimeoutSeconds        = int.TryParse(TxtBackendTimeout.Text, out var t) ? t : 30,
            DevConsoleEnabled            = ChkDevConsole.IsChecked == true,
        };
        SettingsService.Save(settings);
        CloseWithAnimation(true);
    }

    private void Cancel_Click(object s, RoutedEventArgs e) => CloseWithAnimation(false);

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isAnimatingClose) { e.Cancel = true; CloseWithAnimation(false); }
        base.OnClosing(e);
    }

    private void CloseWithAnimation(bool? result)
    {
        if (_isAnimatingClose) return;
        _isAnimatingClose = true;
        _pendingResult = result;
        FluidMotion.MorphClose(RootBorder, WindowScale, WindowTranslate, _originRect, this,
            () => DialogResult = _pendingResult);
    }

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
            if (item.Tag?.ToString() == tag) { combo.SelectedItem = item; return; }
    }

    private static string L(string key)
        => Application.Current.Resources[key] as string ?? key;
}
