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

    [JsonPropertyName("notify_on_complete")]
    public bool NotifyOnComplete { get; set; } = true;

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

    // Developer
    [JsonPropertyName("dev_console_enabled")]
    public bool DevConsoleEnabled { get; set; } = false;

    // Window Blur (Liquid Glass) Settings
    [JsonPropertyName("blur_main_window")]
    public bool BlurMainWindow { get; set; } = false;

    [JsonPropertyName("blur_editor")]
    public bool BlurEditor { get; set; } = false;

    [JsonPropertyName("blur_settings")]
    public bool BlurSettings { get; set; } = false;

    [JsonPropertyName("blur_log_viewer")]
    public bool BlurLogViewer { get; set; } = false;

    [JsonPropertyName("blur_dev_console")]
    public bool BlurDevConsole { get; set; } = false;

    [JsonPropertyName("blur_desc_editor")]
    public bool BlurDescEditor { get; set; } = false;

    [JsonPropertyName("auto_show_progress_window")]
    public bool AutoShowProgressWindow { get; set; } = true;

    [JsonPropertyName("hide_scrollbars")]
    public bool HideScrollbars { get; set; } = false;
}
