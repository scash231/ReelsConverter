using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using ReelsConverterUI.Models;

namespace ReelsConverterUI.Services;

public static class ThemeService
{
    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ReelsConverter", "theme.json");

    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

    public static ThemeSettings Current { get; private set; } = Load();

    public static ThemeSettings Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<ThemeSettings>(
                    File.ReadAllText(_path), _opts) ?? new();
        }
        catch { }
        return new ThemeSettings();
    }

    public static void Save(ThemeSettings theme)
    {
        Current = theme;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(theme, _opts));
    }

    public static void Apply(ThemeSettings theme)
    {
        var res = Application.Current.Resources;
        SetBrush(res, "BgDeep", theme.BgDeep);
        SetBrush(res, "BgSurface", theme.BgSurface);
        SetBrush(res, "BgCard", theme.BgCard);
        SetBrush(res, "BgElevated", theme.BgElevated);
        SetBrush(res, "BorderSub", theme.BorderSub);
        SetBrush(res, "Accent", theme.Accent);
        SetBrush(res, "AccentPink", theme.AccentAlt);
        SetBrush(res, "TextPrimary", theme.TextPrimary);
        SetBrush(res, "TextSec", theme.TextSec);
        SetBrush(res, "SuccessGreen", theme.SuccessGreen);
        SetBrush(res, "ErrorRed", theme.ErrorRed);

        if (TryParseColor(theme.ButtonGrad, out var gc))
        {
            if (res["AccentGrad"] is LinearGradientBrush lgb && !lgb.IsFrozen)
            {
                lgb.GradientStops[0].Color = gc;
                lgb.GradientStops[1].Color = gc;
            }
            else
            {
                res["AccentGrad"] = new LinearGradientBrush(gc, gc, 0);
            }
        }
    }

    private static void SetBrush(ResourceDictionary res, string key, string hex)
    {
        if (!TryParseColor(hex, out var color)) return;
        if (res[key] is SolidColorBrush brush && !brush.IsFrozen)
            brush.Color = color;
        else
            res[key] = new SolidColorBrush(color);
    }

    public static bool TryParseColor(string hex, out Color color)
    {
        try
        {
            color = (Color)ColorConverter.ConvertFromString(hex);
            return true;
        }
        catch
        {
            color = default;
            return false;
        }
    }

    // ── Built-in Presets ──────────────────────────────────────

    public static ThemeSettings DefaultDark => new();

    public static ThemeSettings MidnightBlue => new()
    {
        BgDeep = "#0D1117", BgSurface = "#161B22", BgCard = "#1C2128",
        BgElevated = "#252B35", BorderSub = "#30363D",
        Accent = "#58A6FF", AccentAlt = "#79C0FF",
        TextPrimary = "#C9D1D9", TextSec = "#8B949E",
        SuccessGreen = "#3FB950", ErrorRed = "#F85149",
        ButtonGrad = "#1F3A5F"
    };

    public static ThemeSettings Ocean => new()
    {
        BgDeep = "#0A1628", BgSurface = "#0F1D32", BgCard = "#15243C",
        BgElevated = "#1C2D48", BorderSub = "#2A3F5F",
        Accent = "#4FC3F7", AccentAlt = "#81D4FA",
        TextPrimary = "#B8D4E8", TextSec = "#5A7A94",
        SuccessGreen = "#66BB6A", ErrorRed = "#EF5350",
        ButtonGrad = "#1A3550"
    };

    public static ThemeSettings Forest => new()
    {
        BgDeep = "#0D140D", BgSurface = "#141E14", BgCard = "#1A261A",
        BgElevated = "#223022", BorderSub = "#2E422E",
        Accent = "#81C784", AccentAlt = "#A5D6A7",
        TextPrimary = "#C8DCC8", TextSec = "#6A846A",
        SuccessGreen = "#66BB6A", ErrorRed = "#E57373",
        ButtonGrad = "#2E4A2E"
    };

    public static ThemeSettings Warm => new()
    {
        BgDeep = "#1A1410", BgSurface = "#221B15", BgCard = "#2A221A",
        BgElevated = "#342B22", BorderSub = "#483D32",
        Accent = "#FFB74D", AccentAlt = "#FFCC80",
        TextPrimary = "#D4C4B0", TextSec = "#8A7A68",
        SuccessGreen = "#81C784", ErrorRed = "#E57373",
        ButtonGrad = "#5A4030"
    };

    public static ThemeSettings Rose => new()
    {
        BgDeep = "#1A1018", BgSurface = "#221620", BgCard = "#2A1C28",
        BgElevated = "#342432", BorderSub = "#483248",
        Accent = "#F48FB1", AccentAlt = "#F8BBD0",
        TextPrimary = "#D4C0CC", TextSec = "#8A6880",
        SuccessGreen = "#81C784", ErrorRed = "#E57373",
        ButtonGrad = "#5A3050"
    };
}
