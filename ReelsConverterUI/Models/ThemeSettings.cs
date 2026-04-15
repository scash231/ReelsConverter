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
}
