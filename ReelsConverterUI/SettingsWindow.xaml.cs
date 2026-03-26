using Microsoft.Win32;
using ReelsConverterUI.Models;
using ReelsConverterUI.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ReelsConverterUI;

public partial class SettingsWindow : Window
{
    private string _lang = "de";

    public SettingsWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var s = SettingsService.Current;

        _lang = s.Language;
        UpdateLangButtons();
        ChkAutoPaste.IsChecked = s.AutoPasteOnFocus;

        SelectComboByTag(CmbDefaultPrivacy, s.DefaultPrivacy);
        ChkAutoShorts.IsChecked = s.AutoAddShortsHashtag;
        ChkDefaultFp.IsChecked = s.DefaultFingerprintEnabled;
        SelectComboByTag(CmbDefaultFpMethod, s.DefaultFingerprintMethod);

        TxtDefaultOutputDir.Text = s.DefaultOutputDir;
        ChkDefaultFpDl.IsChecked = s.DefaultFingerprintDlEnabled;
        SelectComboByTag(CmbDefaultFpMethodDl, s.DefaultFingerprintDlMethod);

        TxtBackendUrl.Text = s.BackendUrl;
        TxtBackendTimeout.Text = s.BackendTimeoutSeconds.ToString();
    }

    private void TitleBar_Drag(object s, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void SLangDE_Click(object s, RoutedEventArgs e)
    {
        _lang = "de";
        UpdateLangButtons();
    }

    private void SLangEN_Click(object s, RoutedEventArgs e)
    {
        _lang = "en";
        UpdateLangButtons();
    }

    private void UpdateLangButtons()
    {
        SLangDE.Foreground = _lang == "de"
            ? (Brush)Application.Current.Resources["Accent"]
            : (Brush)Application.Current.Resources["TextSec"];
        SLangEN.Foreground = _lang == "en"
            ? (Brush)Application.Current.Resources["Accent"]
            : (Brush)Application.Current.Resources["TextSec"];
    }

    private void BrowseOutput_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Default save location" };
        if (dlg.ShowDialog() == true)
            TxtDefaultOutputDir.Text = dlg.FolderName;
    }

    private void Save_Click(object s, RoutedEventArgs e)
    {
        var settings = new AppSettings
        {
            Language                = _lang,
            AutoPasteOnFocus        = ChkAutoPaste.IsChecked == true,
            DefaultPrivacy          = (CmbDefaultPrivacy.SelectedItem  as ComboBoxItem)?.Tag?.ToString() ?? "public",
            AutoAddShortsHashtag    = ChkAutoShorts.IsChecked == true,
            DefaultFingerprintEnabled  = ChkDefaultFp.IsChecked == true,
            DefaultFingerprintMethod   = (CmbDefaultFpMethod.SelectedItem   as ComboBoxItem)?.Tag?.ToString() ?? "standard",
            DefaultOutputDir           = TxtDefaultOutputDir.Text.Trim(),
            DefaultFingerprintDlEnabled  = ChkDefaultFpDl.IsChecked == true,
            DefaultFingerprintDlMethod   = (CmbDefaultFpMethodDl.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "standard",
            BackendUrl              = TxtBackendUrl.Text.Trim(),
            BackendTimeoutSeconds   = int.TryParse(TxtBackendTimeout.Text, out var t) ? t : 30,
        };
        SettingsService.Save(settings);
        DialogResult = true;
    }

    private void Cancel_Click(object s, RoutedEventArgs e) => DialogResult = false;

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (item.Tag?.ToString() == tag)
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }
}
