using System.Text.Json.Serialization;

namespace ReelsConverterUI.Models;

public sealed class AppSettings
{
    // General
    [JsonPropertyName("language")]
    public string Language { get; set; } = "de";

    [JsonPropertyName("auto_paste_on_focus")]
    public bool AutoPasteOnFocus { get; set; } = false;

    [JsonPropertyName("always_on_top")]
    public bool AlwaysOnTop { get; set; } = false;

    [JsonPropertyName("auto_fetch_metadata")]
    public bool AutoFetchMetadata { get; set; } = false;

    [JsonPropertyName("completion_notification_mode")]
    public string CompletionNotificationMode { get; set; } = "sound_and_notification";

    [JsonIgnore]
    public bool NotifyOnComplete => CompletionNotificationMode != "off";

    [JsonIgnore]
    public bool EnableNotificationSound => CompletionNotificationMode == "sound_and_notification";

    // Upload defaults
    [JsonPropertyName("default_privacy")]
    public string DefaultPrivacy { get; set; } = "public";

    [JsonPropertyName("auto_add_shorts_hashtag")]
    public bool AutoAddShortsHashtag { get; set; } = true;

    [JsonPropertyName("default_fingerprint_enabled")]
    public bool DefaultFingerprintEnabled { get; set; } = true;

    [JsonPropertyName("default_fingerprint_method")]
    public string DefaultFingerprintMethod { get; set; } = "standard";

    // Download defaults
    [JsonPropertyName("default_output_dir")]
    public string DefaultOutputDir { get; set; } = "";

    [JsonPropertyName("default_video_quality")]
    public string DefaultVideoQuality { get; set; } = "best";

    [JsonPropertyName("default_fingerprint_dl_enabled")]
    public bool DefaultFingerprintDlEnabled { get; set; } = true;

    [JsonPropertyName("default_fingerprint_dl_method")]
    public string DefaultFingerprintDlMethod { get; set; } = "standard";

    // Performance
    [JsonPropertyName("use_gpu")]
    public bool UseGpu { get; set; } = false;

    [JsonPropertyName("max_concurrent_jobs")]
    public int MaxConcurrentJobs { get; set; } = 1;

    // Backend
    [JsonPropertyName("backend_url")]
    public string BackendUrl { get; set; } = "http://127.0.0.1:8765";

    [JsonPropertyName("backend_timeout_seconds")]
    public int BackendTimeoutSeconds { get; set; } = 30;

    [JsonPropertyName("auto_restart_backend")]
    public bool AutoRestartBackend { get; set; } = true;

    [JsonPropertyName("backend_log_level")]
    public string BackendLogLevel { get; set; } = "info";

    // Developer
    [JsonPropertyName("enable_developer_mode")]
    public bool EnableDeveloperMode { get; set; } = false;

    [JsonPropertyName("dev_console_enabled")]
    public bool DevConsoleEnabled { get; set; } = false;

    [JsonPropertyName("auto_open_console_on_error")]
    public bool AutoOpenConsoleOnError { get; set; } = true;

    [JsonPropertyName("verbose_logging")]
    public bool VerboseLogging { get; set; } = false;

    [JsonPropertyName("bypass_file_restrictions")]
    public bool BypassFileRestrictions { get; set; } = false;

    [JsonPropertyName("show_performance_overlay")]
    public bool ShowPerformanceOverlay { get; set; } = false;

    // Window Blur (Liquid Glass) Settings
    [JsonPropertyName("blur_main_window")]
    public bool BlurMainWindow { get; set; } = true;

    [JsonPropertyName("blur_editor")]
    public bool BlurEditor { get; set; } = true;

    [JsonPropertyName("blur_settings")]
    public bool BlurSettings { get; set; } = true;

    [JsonPropertyName("blur_log_viewer")]
    public bool BlurLogViewer { get; set; } = true;

    [JsonPropertyName("blur_dev_console")]
    public bool BlurDevConsole { get; set; } = true;

    [JsonPropertyName("blur_desc_editor")]
    public bool BlurDescEditor { get; set; } = true;

    [JsonPropertyName("auto_show_progress_window")]
    public bool AutoShowProgressWindow { get; set; } = true;

    [JsonPropertyName("hide_scrollbars")]
    public bool HideScrollbars { get; set; } = false;

    // Console Logging Filters
    [JsonPropertyName("console_show_system")]
    public bool ConsoleShowSystem { get; set; } = true;

    [JsonPropertyName("console_show_backend")]
    public bool ConsoleShowBackend { get; set; } = true;

    [JsonPropertyName("console_show_ffmpeg")]
    public bool ConsoleShowFFmpeg { get; set; } = true;

    [JsonPropertyName("compact_mode")]
    public bool CompactMode { get; set; } = false;

    [JsonPropertyName("show_window_resizer_grip")]
    public bool ShowWindowResizerGrip { get; set; } = true;

    [JsonPropertyName("resizer_grip_only_on_hover")]
    public bool ResizerGripOnlyOnHover { get; set; } = false;

    // Window Sizing Settings
    [JsonPropertyName("main_window_width")]
    public double MainWindowWidth { get; set; } = 760;

    [JsonPropertyName("main_window_height")]
    public double MainWindowHeight { get; set; } = 560;

    [JsonPropertyName("editor_window_width")]
    public double EditorWindowWidth { get; set; } = 940;

    [JsonPropertyName("editor_window_height")]
    public double EditorWindowHeight { get; set; } = 620;

    [JsonPropertyName("settings_window_width")]
    public double SettingsWindowWidth { get; set; } = 680;

    [JsonPropertyName("settings_window_height")]
    public double SettingsWindowHeight { get; set; } = 500;

    [JsonPropertyName("designer_window_width")]
    public double DesignerWindowWidth { get; set; } = 680;

    [JsonPropertyName("designer_window_height")]
    public double DesignerWindowHeight { get; set; } = 500;

    [JsonPropertyName("desc_editor_window_width")]
    public double DescEditorWindowWidth { get; set; } = 760;

    [JsonPropertyName("desc_editor_window_height")]
    public double DescEditorWindowHeight { get; set; } = 560;

    [JsonPropertyName("dev_console_window_width")]
    public double DevConsoleWindowWidth { get; set; } = 520;

    [JsonPropertyName("dev_console_window_height")]
    public double DevConsoleWindowHeight { get; set; } = 320;

    [JsonPropertyName("log_viewer_window_width")]
    public double LogViewerWindowWidth { get; set; } = 500;

    [JsonPropertyName("log_viewer_window_height")]
    public double LogViewerWindowHeight { get; set; } = 360;

    public AppSettings Clone()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(this);
        return System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }
}
