using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ReelsConverterUI.Services;

public static class WindowBlurHelper
{
    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    private enum WindowCompositionAttribute
    {
        WCA_ACCENT_POLICY = 19
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    private enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTBACKGROUND = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
        ACCENT_INVALID_STATE = 5
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    public static void EnableBlur(Window window)
    {
        try
        {
            var windowHelper = new WindowInteropHelper(window);
            var hwnd = windowHelper.Handle;

            // Set Windows 11 rounded corners preference to match WPF RootBorder CornerRadius
            int cornerPreference = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

            // Set up frosted glass composition blur
            var accent = new AccentPolicy();
            accent.AccentState = AccentState.ACCENT_ENABLE_BLURBEHIND;
            accent.GradientColor = 0; // fully transparent blend so the background blur dominates

            var accentStructSize = Marshal.SizeOf(accent);
            var accentPtr = Marshal.AllocHGlobal(accentStructSize);
            Marshal.StructureToPtr(accent, accentPtr, false);

            var data = new WindowCompositionAttributeData();
            data.Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY;
            data.SizeOfData = accentStructSize;
            data.Data = accentPtr;

            SetWindowCompositionAttribute(hwnd, ref data);

            Marshal.FreeHGlobal(accentPtr);
        }
        catch
        {
            // Fallback if OS doesn't support it
        }
    }

    public static void DisableBlur(Window window)
    {
        try
        {
            var windowHelper = new WindowInteropHelper(window);
            var hwnd = windowHelper.Handle;

            var accent = new AccentPolicy();
            accent.AccentState = AccentState.ACCENT_DISABLED;

            var accentStructSize = Marshal.SizeOf(accent);
            var accentPtr = Marshal.AllocHGlobal(accentStructSize);
            Marshal.StructureToPtr(accent, accentPtr, false);

            var data = new WindowCompositionAttributeData();
            data.Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY;
            data.SizeOfData = accentStructSize;
            data.Data = accentPtr;

            SetWindowCompositionAttribute(hwnd, ref data);

            Marshal.FreeHGlobal(accentPtr);
        }
        catch
        {
            // Ignore
        }
    }

    public static void EnableBlurWithFade(Window window, System.Windows.Controls.Border rootBorder)
    {
        try
        {
            if (rootBorder.Background is SolidColorBrush currentBrush)
            {
                var targetColor = currentBrush.Color;

                // If already solid, just enable blur and return
                if (targetColor.A == 255)
                {
                    EnableBlur(window);
                    return;
                }

                // 1. Create a fully solid version of the background
                var solidColor = Color.FromRgb(targetColor.R, targetColor.G, targetColor.B);
                var animBrush = new SolidColorBrush(solidColor);
                rootBorder.Background = animBrush;

                // 2. Enable composition blur
                EnableBlur(window);

                // 3. Smoothly fade the color alpha down to targetColor
                var anim = new ColorAnimation(targetColor, TimeSpan.FromMilliseconds(420))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                animBrush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
            }
            else
            {
                EnableBlur(window);
            }
        }
        catch
        {
            EnableBlur(window);
        }
    }
}
