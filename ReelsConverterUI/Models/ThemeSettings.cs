using System.Text.Json.Serialization;

namespace ReelsConverterUI.Models;

public sealed class ThemeSettings
{
    [JsonPropertyName("bg_deep")]
    public string BgDeep { get; set; } = "#141416";

    [JsonPropertyName("bg_surface")]
    public string BgSurface { get; set; } = "#1B1B1E";

    [JsonPropertyName("bg_card")]
    public string BgCard { get; set; } = "#202024";

    [JsonPropertyName("bg_elevated")]
    public string BgElevated { get; set; } = "#2A2A2E";

    [JsonPropertyName("border_sub")]
    public string BorderSub { get; set; } = "#38383D";

    [JsonPropertyName("accent")]
    public string Accent { get; set; } = "#7A9EC0";

    [JsonPropertyName("accent_alt")]
    public string AccentAlt { get; set; } = "#7A9EC0";

    [JsonPropertyName("text_primary")]
    public string TextPrimary { get; set; } = "#BCBCC2";

    [JsonPropertyName("text_sec")]
    public string TextSec { get; set; } = "#68686E";

    [JsonPropertyName("success")]
    public string SuccessGreen { get; set; } = "#5AAF6E";

    [JsonPropertyName("error")]
    public string ErrorRed { get; set; } = "#C44848";

    [JsonPropertyName("button_grad")]
    public string ButtonGrad { get; set; } = "#38485A";

    [JsonPropertyName("adaptive_thumbnail_theme")]
    public bool AdaptiveThumbnailTheme { get; set; } = false;

    [JsonPropertyName("animation_level")]
    public string AnimationLevel { get; set; } = "Smooth Liquid";

    [JsonPropertyName("preset_name")]
    public string PresetName { get; set; } = "Custom Theme";

    [JsonPropertyName("enable_borders")]
    public bool EnableBorders { get; set; } = true;

    [JsonPropertyName("gradient_effect_mode")]
    public string GradientEffectMode { get; set; } = "thumbnail_only";

    [JsonPropertyName("bg_gradient_strength")]
    public double BgGradientStrength { get; set; } = 1.0;

    [JsonPropertyName("enable_bg_gradient")]
    public bool EnableBgGradient { get; set; } = true;

    [JsonPropertyName("enable_thumbnail_gradient")]
    public bool EnableThumbnailGradient { get; set; } = true;

    [JsonPropertyName("thumbnail_gradient_only")]
    public bool ThumbnailGradientOnly { get; set; } = false;

    [JsonPropertyName("disable_thumbnail_card")]
    public bool DisableThumbnailCard { get; set; } = false;

    [JsonPropertyName("animation_preset")]
    public string AnimationPreset { get; set; } = "Smooth Liquid";

    [JsonPropertyName("window_anim_duration")]
    public int WindowAnimDuration { get; set; } = 380;

    [JsonPropertyName("window_anim_easing")]
    public string WindowAnimEasing { get; set; } = "Apple Spring (Bouncy)";

    [JsonPropertyName("window_anim_style")]
    public string WindowAnimStyle { get; set; } = "Morph & Scale";

    [JsonPropertyName("button_hover_scale")]
    public double ButtonHoverScale { get; set; } = 1.03;

    [JsonPropertyName("enable_hover_micro_anims")]
    public bool EnableHoverMicroAnims { get; set; } = true;

    [JsonPropertyName("progress_bar_anim_speed")]
    public int ProgressBarAnimSpeed { get; set; } = 350;

    [JsonPropertyName("enable_progress_pulse")]
    public bool EnableProgressPulse { get; set; } = true;

    [JsonPropertyName("dropdown_anim_style")]
    public string DropdownAnimStyle { get; set; } = "Scale & Fade";

    [JsonPropertyName("tab_switch_transition")]
    public string TabSwitchTransition { get; set; } = "Slide & Fade";

    [JsonPropertyName("enable_staggered_animations")]
    public bool EnableStaggeredAnimations { get; set; } = true;

    public ThemeSettings Clone()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(this);
        return System.Text.Json.JsonSerializer.Deserialize<ThemeSettings>(json) ?? new ThemeSettings();
    }
}
