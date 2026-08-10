using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using ReelsConverterUI.Models;

namespace ReelsConverterUI.Services;

public static class ThemeService
{
    public static event Action? ThemeApplied;

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

        // Detect if light theme by checking background deep brightness
        bool isLight = false;
        if (TryParseColor(theme.BgDeep, out var bgDeepColor))
        {
            // Standard relative luminance formula
            double luminance = (0.2126 * bgDeepColor.R + 0.7152 * bgDeepColor.G + 0.0722 * bgDeepColor.B) / 255.0;
            isLight = luminance > 0.5;
        }

        // Auto-enforce contrast for TextPrimary, TextSec, and BorderSub on dark and light backgrounds
        string primaryHex = theme.TextPrimary;
        string secHex = theme.TextSec;
        string borderSubHex = theme.BorderSub;

        if (!isLight)
        {
            if (TryParseColor(theme.TextPrimary, out var pCol))
            {
                double pLum = (0.2126 * pCol.R + 0.7152 * pCol.G + 0.0722 * pCol.B) / 255.0;
                if (pLum < 0.55) primaryHex = "#F1F5F9"; // Crisp bright white-slate fallback
            }
            else primaryHex = "#F1F5F9";

            if (TryParseColor(theme.TextSec, out var sCol))
            {
                double sLum = (0.2126 * sCol.R + 0.7152 * sCol.G + 0.0722 * sCol.B) / 255.0;
                if (sLum < 0.40) secHex = "#94A3B8"; // High-contrast silver secondary text fallback
            }
            else secHex = "#94A3B8";

            // Subtle dark charcoal border for dark mode so no bright border outlines appear
            borderSubHex = "#38383D";
        }
        else
        {
            if (TryParseColor(theme.TextPrimary, out var pCol))
            {
                double pLum = (0.2126 * pCol.R + 0.7152 * pCol.G + 0.0722 * pCol.B) / 255.0;
                if (pLum > 0.35) primaryHex = "#0F172A"; // Crisp dark slate text for light background
            }
            else primaryHex = "#0F172A";

            if (TryParseColor(theme.TextSec, out var sCol))
            {
                double sLum = (0.2126 * sCol.R + 0.7152 * sCol.G + 0.0722 * sCol.B) / 255.0;
                if (sLum > 0.35) secHex = "#334155"; // Dark slate secondary text for light background
            }
            else secHex = "#334155";

            if (TryParseColor(theme.BorderSub, out var bCol))
            {
                double bLum = (0.2126 * bCol.R + 0.7152 * bCol.G + 0.0722 * bCol.B) / 255.0;
                if (bLum > 0.50 || bLum < 0.15) borderSubHex = "#64748B"; // Crisp distinct border for light mode
            }
            else borderSubHex = "#64748B";
        }

        byte deepAlpha = 255;
        if (TryParseColor(theme.BgDeep, out var deepCol))
        {
            deepAlpha = deepCol.A;
        }

        string effectiveBorderSub = theme.EnableBorders ? borderSubHex : "#00000000";

        SetBrush(res, "BgDeep", theme.BgDeep, deepAlpha);
        SetBrush(res, "BgSurface", theme.BgSurface, deepAlpha);
        SetBrush(res, "BgCard", theme.BgCard, deepAlpha);
        SetBrush(res, "BgElevated", theme.BgElevated, deepAlpha);
        SetBrush(res, "BorderSub", effectiveBorderSub);
        SetBrush(res, "Accent", theme.Accent);
        SetBrush(res, "AccentPink", theme.AccentAlt);
        SetBrush(res, "TextPrimary", primaryHex);
        SetBrush(res, "TextSec", secHex);
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

        res["HeaderGrad"] = res["BgSurface"];
        res["BgGrad"] = res["BgDeep"];
        res["ThumbGrad"] = res["InputBg"];

        // Programmatically expose helper brushes for hover, active, and dropdown/popup states
        if (isLight)
        {
            res["HoverBg"] = new SolidColorBrush(Color.FromArgb(0x12, 0x00, 0x00, 0x00));  // 7% black
            res["ActiveBg"] = new SolidColorBrush(Color.FromArgb(0x1D, 0x00, 0x00, 0x00)); // 11% black
            res["CardHoverBg"] = new SolidColorBrush(Color.FromArgb(0x0A, 0x00, 0x00, 0x00));
            res["PopupBg"] = new SolidColorBrush(Color.FromArgb(0xFA, 0xF5, 0xF6, 0xF8)); // 98% solid light popup
            res["PopupBorder"] = new SolidColorBrush(Color.FromArgb(0x38, 0x64, 0x74, 0x8B));
            res["InputBg"] = new SolidColorBrush(Color.FromArgb(0x12, 0x00, 0x00, 0x00)); // 7% black input bg for light mode
        }
        else
        {
            res["HoverBg"] = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));  // 9% white
            res["ActiveBg"] = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF)); // 14% white
            res["CardHoverBg"] = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));
            res["PopupBg"] = new SolidColorBrush(Color.FromArgb(0xFA, 0x1E, 0x1E, 0x24)); // 98% solid dark popup
            res["PopupBorder"] = new SolidColorBrush(Color.FromArgb(0x38, 0x38, 0x38, 0x3D));
            res["InputBg"] = new SolidColorBrush(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF)); // 4% white input bg for dark mode
        }

        ThemeApplied?.Invoke();
    }

    private static void SetBrush(ResourceDictionary res, string key, string hex, byte deepAlpha = 255)
    {
        if (!TryParseColor(hex, out var color)) return;

        // Apply glassmorphism transparency to background layers if deepAlpha < 255 or color.A == 255
        if (deepAlpha < 255)
        {
            if (key == "BgCard")
                color.A = (byte)Math.Clamp(Math.Round(deepAlpha * 0.85), 20, 255);
            else if (key == "BgSurface")
                color.A = (byte)Math.Clamp(Math.Round(deepAlpha * 0.90), 25, 255);
            else if (key == "BgElevated")
                color.A = (byte)Math.Clamp(Math.Round(deepAlpha * 0.95), 30, 255);
            else if (key == "BgDeep")
                color.A = deepAlpha;
        }
        else if (color.A == 255)
        {
            if (key == "BgCard")
                color.A = 0xAC; // ~67% opacity for glass cards
            else if (key == "BgSurface")
                color.A = 0xBD; // ~74% opacity
            else if (key == "BgElevated")
                color.A = 0xCD; // ~80% opacity
        }

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

    public static ThemeSettings Aurora => new()
    {
        BgDeep = "#150F18", BgSurface = "#1E1422", BgCard = "#261A2C",
        BgElevated = "#312138", BorderSub = "#442E4C",
        Accent = "#EC4899", AccentAlt = "#F472B6",
        TextPrimary = "#E2D9E6", TextSec = "#A08DA5",
        SuccessGreen = "#22C55E", ErrorRed = "#EF4444",
        ButtonGrad = "#4F1E65"
    };

    public static ThemeSettings Cyberpunk => new()
    {
        BgDeep = "#0A0A0C", BgSurface = "#121216", BgCard = "#181820",
        BgElevated = "#22222B", BorderSub = "#2F2F3D",
        Accent = "#06B6D4", AccentAlt = "#22D3EE",
        TextPrimary = "#E2E8F0", TextSec = "#94A3B8",
        SuccessGreen = "#10B981", ErrorRed = "#F43F5E",
        ButtonGrad = "#1B3B48"
    };

    public static ThemeSettings Nordic => new()
    {
        BgDeep = "#0F172A", BgSurface = "#1E293B", BgCard = "#334155",
        BgElevated = "#475569", BorderSub = "#64748B",
        Accent = "#38BDF8", AccentAlt = "#7DD3FC",
        TextPrimary = "#F1F5F9", TextSec = "#94A3B8",
        SuccessGreen = "#34D399", ErrorRed = "#F87171",
        ButtonGrad = "#2E4A62"
    };

    public static ThemeSettings OledDark => new()
    {
        BgDeep = "#000000", BgSurface = "#080808", BgCard = "#111111",
        BgElevated = "#181818", BorderSub = "#262626",
        Accent = "#38B6FF", AccentAlt = "#A5F3FC",
        TextPrimary = "#F8FAFC", TextSec = "#64748B",
        SuccessGreen = "#10B981", ErrorRed = "#F43F5E",
        ButtonGrad = "#1F2937"
    };

    public static ThemeSettings Emerald => new()
    {
        BgDeep = "#051610", BgSurface = "#0B221B", BgCard = "#102E24",
        BgElevated = "#173E31", BorderSub = "#225645",
        Accent = "#10B981", AccentAlt = "#34D399",
        TextPrimary = "#ECFDF5", TextSec = "#6EE7B7",
        SuccessGreen = "#10B981", ErrorRed = "#F43F5E",
        ButtonGrad = "#064E3B"
    };

    public static ThemeSettings Dracula => new()
    {
        BgDeep = "#1E1E2E", BgSurface = "#252538", BgCard = "#2E2E44",
        BgElevated = "#383852", BorderSub = "#444462",
        Accent = "#FF79C6", AccentAlt = "#BD93F9",
        TextPrimary = "#F8F8F2", TextSec = "#6272A4",
        SuccessGreen = "#50FA7B", ErrorRed = "#FF5555",
        ButtonGrad = "#4D3D70"
    };

    public static ThemeSettings Alabaster => new()
    {
        BgDeep = "#E0F5F6F8", BgSurface = "#AAF5F6F8", BgCard = "#80FFFFFF",
        BgElevated = "#90E9ECEF", BorderSub = "#40000000",
        Accent = "#007AFF", AccentAlt = "#0055B3",
        TextPrimary = "#1F2329", TextSec = "#5C6370",
        SuccessGreen = "#28A745", ErrorRed = "#DC3545",
        ButtonGrad = "#007AFF"
    };

    public static ThemeSettings Sandstone => new()
    {
        BgDeep = "#E0F4EFEA", BgSurface = "#AAF7F2EB", BgCard = "#80FAF6F0",
        BgElevated = "#90EBE4DB", BorderSub = "#405C4F44",
        Accent = "#D97706", AccentAlt = "#B45309",
        TextPrimary = "#2C2520", TextSec = "#786B60",
        SuccessGreen = "#10B981", ErrorRed = "#EF4444",
        ButtonGrad = "#8B4F30"
    };

    public static ThemeSettings MidnightGold => new()
    {
        BgDeep = "#0B0C10", BgSurface = "#111318", BgCard = "#181A22",
        BgElevated = "#222530", BorderSub = "#2E3342",
        Accent = "#D4AF37", AccentAlt = "#FFD700",
        TextPrimary = "#F5F7FA", TextSec = "#8C92A6",
        SuccessGreen = "#10B981", ErrorRed = "#EF4444",
        ButtonGrad = "#9A7B1C"
    };

    public static ThemeSettings SunsetGlow => new()
    {
        BgDeep = "#180F0A", BgSurface = "#201610", BgCard = "#2C1E16",
        BgElevated = "#38291F", BorderSub = "#4E3A2F",
        Accent = "#F97316", AccentAlt = "#FB923C",
        TextPrimary = "#FDE8E0", TextSec = "#A78B7E",
        SuccessGreen = "#10B981", ErrorRed = "#EF4444",
        ButtonGrad = "#C2410C"
    };

    public static ThemeSettings Amethyst => new()
    {
        BgDeep = "#0F0B18", BgSurface = "#171224", BgCard = "#201A30",
        BgElevated = "#2A223E", BorderSub = "#3C2F59",
        Accent = "#A855F7", AccentAlt = "#C084FC",
        TextPrimary = "#F3E8FF", TextSec = "#A78BFA",
        SuccessGreen = "#10B981", ErrorRed = "#EF4444",
        ButtonGrad = "#7E22CE"
    };

    public static ThemeSettings MintFresh => new()
    {
        BgDeep = "#D0F2ECF0", BgSurface = "#A0EAF0E6", BgCard = "#80EDF8F5",
        BgElevated = "#90E0F0EB", BorderSub = "#300D4D3D",
        Accent = "#059669", AccentAlt = "#10B981",
        TextPrimary = "#111827", TextSec = "#4B5563",
        SuccessGreen = "#10B981", ErrorRed = "#EF4444",
        ButtonGrad = "#047857"
    };

    public static ThemeSettings CrimsonRed => new()
    {
        BgDeep = "#12080A", BgSurface = "#1A0E10", BgCard = "#251518",
        BgElevated = "#301B1E", BorderSub = "#4A272D",
        Accent = "#E11D48", AccentAlt = "#F43F5E",
        TextPrimary = "#FFE4E6", TextSec = "#FDA4AF",
        SuccessGreen = "#10B981", ErrorRed = "#EF4444",
        ButtonGrad = "#BE123C"
    };

    public static ThemeSettings CyberLime => new()
    {
        BgDeep = "#0A0D0A", BgSurface = "#101610", BgCard = "#162016",
        BgElevated = "#202C20", BorderSub = "#2E3F2E",
        Accent = "#84CC16", AccentAlt = "#A3E635",
        TextPrimary = "#ECFCCB", TextSec = "#A3E635",
        SuccessGreen = "#22C55E", ErrorRed = "#EF4444",
        ButtonGrad = "#4D7C0F"
    };

    private static readonly string _customPresetsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ReelsConverter", "custom_presets.json");

    public static System.Collections.Generic.List<ThemeSettings> LoadCustomPresets()
    {
        try
        {
            if (File.Exists(_customPresetsPath))
            {
                return JsonSerializer.Deserialize<System.Collections.Generic.List<ThemeSettings>>(
                    File.ReadAllText(_customPresetsPath), _opts) ?? new();
            }
        }
        catch { }
        return new System.Collections.Generic.List<ThemeSettings>();
    }

    public static void SaveCustomPresets(System.Collections.Generic.List<ThemeSettings> presets)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_customPresetsPath)!);
            File.WriteAllText(_customPresetsPath, JsonSerializer.Serialize(presets, _opts));
        }
        catch { }
    }
}
