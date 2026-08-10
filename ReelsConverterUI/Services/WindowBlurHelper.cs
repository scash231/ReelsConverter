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

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
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

            // Clip window region to match XAML rounded rect CornerRadius
            ApplyRoundedRegion(window);

            window.SizeChanged -= Window_SizeChangedForRegion;
            window.SizeChanged += Window_SizeChangedForRegion;
        }
        catch
        {
            // Fallback if OS doesn't support it
        }
    }

    private static void Window_SizeChangedForRegion(object sender, SizeChangedEventArgs e)
    {
        if (sender is Window win)
        {
            ApplyRoundedRegion(win);
        }
    }

    public static void ApplyRoundedRegion(Window window)
    {
        try
        {
            var windowHelper = new WindowInteropHelper(window);
            var hwnd = windowHelper.Handle;
            if (hwnd == IntPtr.Zero) return;

            if (!GetWindowRect(hwnd, out RECT rect)) return;
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0) return;

            double radius = 16;
            if (window.Content is FrameworkElement root)
            {
                var border = root as System.Windows.Controls.Border ?? LogicalTreeHelper.FindLogicalNode(root, "RootBorder") as System.Windows.Controls.Border;
                if (border != null)
                {
                    radius = border.CornerRadius.TopLeft;
                }
            }

            var source = PresentationSource.FromVisual(window);
            double scaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double scaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            int cornerEllipseX = (int)Math.Round(radius * 2 * scaleX);
            int cornerEllipseY = (int)Math.Round(radius * 2 * scaleY);

            IntPtr hRgn = CreateRoundRectRgn(0, 0, width + 1, height + 1, cornerEllipseX, cornerEllipseY);
            if (hRgn != IntPtr.Zero)
            {
                SetWindowRgn(hwnd, hRgn, true);
            }
        }
        catch
        {
            // Ignore
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
