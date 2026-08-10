using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ReelsConverterUI.Models;

namespace ReelsConverterUI.Animations;

// Exact CASpringAnimation / SwiftUI .spring(response:dampingFraction:) formula
public sealed class AppleSpringEase : IEasingFunction
{
    private readonly double _zeta;
    private readonly double _omega0;
    private readonly double _settlingTime;

    public AppleSpringEase(double dampingRatio = 0.86, double response = 0.4)
    {
        _zeta = Math.Clamp(dampingRatio, 0.01, 5.0);
        _omega0 = 2.0 * Math.PI / Math.Max(response, 0.001);
        _settlingTime = Math.Log(1000.0) / (_zeta * _omega0);
    }

    public double Ease(double t)
    {
        if (t <= 0.0) return 0.0;
        if (t >= 1.0) return 1.0;
        double realTime = t * _settlingTime;

        if (_zeta < 1.0)
        {
            // Under-damped
            double omegaD = _omega0 * Math.Sqrt(1.0 - _zeta * _zeta);
            double envelope = Math.Exp(-_zeta * _omega0 * realTime);
            double osc = Math.Cos(omegaD * realTime)
                       + (_zeta * _omega0 / omegaD) * Math.Sin(omegaD * realTime);
            return 1.0 - envelope * osc;
        }

        if (Math.Abs(_zeta - 1.0) < 1e-6)
        {
            // Critically damped
            double wt = _omega0 * realTime;
            return 1.0 - (1.0 + wt) * Math.Exp(-wt);
        }

        // Over-damped
        double s = Math.Sqrt(_zeta * _zeta - 1.0);
        double r1 = -_omega0 * (_zeta + s);
        double r2 = -_omega0 * (_zeta - s);
        double c2 = 1.0 / (2.0 * s);
        double c1 = 1.0 - c2;
        return 1.0 - c1 * Math.Exp(r1 * realTime) - c2 * Math.Exp(r2 * realTime);
    }

    public static AppleSpringEase Interactive => new(0.72, 0.50);
    public static AppleSpringEase Gentle      => new(0.80, 0.55);
    public static AppleSpringEase Bouncy      => new(0.65, 0.45);
    public static AppleSpringEase Smooth      => new(1.00, 0.40);
    public static AppleSpringEase Snappy      => new(0.86, 0.35);
}

public static class FluidMotion
{
    private static void GetWindowPosition(Window window, double winW, double winH, out double left, out double top)
    {
        left = window.Left;
        top = window.Top;

        if (double.IsNaN(left) || double.IsNaN(top))
        {
            if (window.Owner != null)
            {
                var ownerW = window.Owner.ActualWidth;
                if (double.IsNaN(ownerW) || ownerW <= 0) ownerW = window.Owner.Width;
                var ownerH = window.Owner.ActualHeight;
                if (double.IsNaN(ownerH) || ownerH <= 0) ownerH = window.Owner.Height;

                var ownerL = window.Owner.Left;
                var ownerT = window.Owner.Top;

                if (double.IsNaN(ownerL)) ownerL = 0;
                if (double.IsNaN(ownerT)) ownerT = 0;

                if (double.IsNaN(left)) left = ownerL + (ownerW - winW) / 2;
                if (double.IsNaN(top)) top = ownerT + (ownerH - winH) / 2;
            }
            else
            {
                if (double.IsNaN(left)) left = (SystemParameters.PrimaryScreenWidth - winW) / 2;
                if (double.IsNaN(top)) top = (SystemParameters.PrimaryScreenHeight - winH) / 2;
            }
        }

        if (double.IsNaN(left)) left = 0;
        if (double.IsNaN(top)) top = 0;
    }

    private static ThemeSettings GetThemeSettings()
    {
        try
        {
            return Services.ThemeService.Current ?? new ThemeSettings();
        }
        catch
        {
            return new ThemeSettings();
        }
    }

    private static string GetAnimationLevel()
    {
        try
        {
            var theme = GetThemeSettings();
            return theme.AnimationPreset ?? theme.AnimationLevel ?? "Balanced";
        }
        catch
        {
            return "Balanced";
        }
    }

    private static IEasingFunction? GetEasing(string? easingName)
    {
        return easingName switch
        {
            "Apple Spring (Bouncy)" => AppleSpringEase.Bouncy,
            "Elastic Snap"          => AppleSpringEase.Snappy,
            "Cubic Smooth"          => AppleSpringEase.Smooth,
            "Linear Constant"       => null,
            _                       => AppleSpringEase.Interactive
        };
    }

    public static void MorphOpen(
        Border root,
        ScaleTransform scale,
        TranslateTransform translate,
        Rect origin,
        Window window,
        Action? onCompleted = null)
    {
        var theme = GetThemeSettings();
        string preset = theme.AnimationPreset ?? theme.AnimationLevel ?? "Balanced";
        if (preset == "Disabled (Static)" || preset == "Disabled" || preset == "None")
        {
            root.Opacity = 1;
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            translate.X = 0;
            translate.Y = 0;
            onCompleted?.Invoke();
            return;
        }

        double speedMult = preset switch
        {
            "Subtle" => 0.7,
            "Smooth Liquid" => 1.35,
            "Hyper Fluid" => 1.65,
            _ => 1.0
        };

        int baseDurMs = theme.WindowAnimDuration > 0 ? theme.WindowAnimDuration : 280;
        var springDur = TimeSpan.FromMilliseconds(baseDurMs * speedMult);
        var fadeDur = TimeSpan.FromMilliseconds((baseDurMs * 0.6) * speedMult);
        var easing = GetEasing(theme.WindowAnimEasing);
        var smooth = AppleSpringEase.Smooth;

        string style = theme.WindowAnimStyle ?? "Morph & Scale";

        if (style == "Fade Only")
        {
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            translate.X = 0;
            translate.Y = 0;
            var fade = new DoubleAnimation(0, 1, fadeDur) { EasingFunction = smooth };
            if (onCompleted != null) fade.Completed += (s, e) => onCompleted();
            root.BeginAnimation(UIElement.OpacityProperty, fade);
            return;
        }

        if (style == "Slide Up & Fade")
        {
            root.RenderTransformOrigin = new Point(0.5, 0.5);
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            translate.X = 0;
            translate.Y = 35;
            root.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, fadeDur) { EasingFunction = smooth });
            var yAnim = new DoubleAnimation(35, 0, springDur) { EasingFunction = easing };
            if (onCompleted != null) yAnim.Completed += (s, e) => onCompleted();
            translate.BeginAnimation(TranslateTransform.YProperty, yAnim);
            return;
        }

        if (style == "Zoom Elastic")
        {
            root.RenderTransformOrigin = new Point(0.5, 0.5);
            scale.ScaleX = 0.6;
            scale.ScaleY = 0.6;
            translate.X = 0;
            translate.Y = 0;
            root.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, fadeDur) { EasingFunction = smooth });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.6, 1.0, springDur) { EasingFunction = AppleSpringEase.Bouncy });
            var yAnim = new DoubleAnimation(0.6, 1.0, springDur) { EasingFunction = AppleSpringEase.Bouncy };
            if (onCompleted != null) yAnim.Completed += (s, e) => onCompleted();
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, yAnim);
            return;
        }

        // Default: Morph & Scale
        var winW = window.ActualWidth;
        if (double.IsNaN(winW) || winW <= 0) winW = window.Width;
        if (double.IsNaN(winW) || winW <= 0) winW = 800;

        var winH = window.ActualHeight;
        if (double.IsNaN(winH) || winH <= 0) winH = window.Height;
        if (double.IsNaN(winH) || winH <= 0) winH = 600;

        double left, top;
        GetWindowPosition(window, winW, winH, out left, out top);

        var btnCx = origin.X + origin.Width / 2;
        var btnCy = origin.Y + origin.Height / 2;

        double ox = Math.Clamp((btnCx - left) / winW, 0.0, 1.0);
        double oy = Math.Clamp((btnCy - top) / winH, 0.0, 1.0);
        root.RenderTransformOrigin = new Point(ox, oy);

        double sx = Math.Clamp(origin.Width / winW, 0.03, 0.45);
        double sy = Math.Clamp(origin.Height / winH, 0.03, 0.45);

        double tx = btnCx - left - ox * winW;
        double ty = btnCy - top - oy * winH;

        root.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, fadeDur) { EasingFunction = smooth });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(sx, 1, springDur) { EasingFunction = easing });

        var yMorphAnim = new DoubleAnimation(sy, 1, springDur) { EasingFunction = easing };
        if (onCompleted != null) yMorphAnim.Completed += (s, e) => onCompleted();
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, yMorphAnim);

        translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(tx, 0, springDur) { EasingFunction = smooth });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(ty, 0, springDur) { EasingFunction = smooth });
    }

    public static void MorphClose(
        Border root,
        ScaleTransform scale,
        TranslateTransform translate,
        Rect origin,
        Window window,
        Action onCompleted)
    {
        var theme = GetThemeSettings();
        string preset = theme.AnimationPreset ?? theme.AnimationLevel ?? "Balanced";
        if (preset == "Disabled (Static)" || preset == "Disabled" || preset == "None")
        {
            try { Services.WindowBlurHelper.DisableBlur(window); } catch { }
            onCompleted();
            return;
        }

        try { Services.WindowBlurHelper.DisableBlur(window); } catch { }

        int baseDurMs = theme.WindowAnimDuration > 0 ? theme.WindowAnimDuration : 280;
        var dur = TimeSpan.FromMilliseconds(baseDurMs * 0.75);
        var ease = GetEasing(theme.WindowAnimEasing);

        var fade = new DoubleAnimation(1, 0, dur) { EasingFunction = ease };
        fade.Completed += (s, e) => onCompleted();

        root.BeginAnimation(UIElement.OpacityProperty, fade);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.9, dur) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.9, dur) { EasingFunction = ease });
    }

    // Stagger children with depth-aware spring (travel increases per child)
    public static void StaggerIn(Panel panel, int baseDelayMs = 60, int stepMs = 50)
    {
        string mode = GetAnimationLevel();
        if (mode == "None")
        {
            foreach (UIElement child in panel.Children)
            {
                if (child is not FrameworkElement fe) continue;
                fe.Opacity = 1;
                fe.RenderTransform = Transform.Identity;
            }
            return;
        }
        if (mode == "Reduced")
        {
            int idx = 0;
            foreach (UIElement child in panel.Children)
            {
                if (child is not FrameworkElement fe) continue;
                fe.Opacity = 0;
                fe.RenderTransform = Transform.Identity;
                var delay = TimeSpan.FromMilliseconds(idx * 30);
                fe.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
                    { BeginTime = delay, EasingFunction = AppleSpringEase.Smooth });
                idx++;
            }
            return;
        }

        var spring = AppleSpringEase.Bouncy;
        var smooth = AppleSpringEase.Gentle;
        int idxOrig = 0;

        foreach (UIElement child in panel.Children)
        {
            if (child is not FrameworkElement fe) continue;

            fe.Opacity = 0;
            fe.RenderTransformOrigin = new Point(0.5, 0.0);
            var group = new TransformGroup();
            var st = new ScaleTransform(0.96 - idxOrig * 0.003, 0.96 - idxOrig * 0.003);
            var tt = new TranslateTransform(0, 20 + idxOrig * 3);
            group.Children.Add(st);
            group.Children.Add(tt);
            fe.RenderTransform = group;

            var delay = TimeSpan.FromMilliseconds(baseDelayMs + idxOrig * stepMs);

            fe.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
                { BeginTime = delay, EasingFunction = smooth });

            tt.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(20 + idxOrig * 3, 0, TimeSpan.FromMilliseconds(520))
                { BeginTime = delay, EasingFunction = spring });

            st.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.96 - idxOrig * 0.003, 1, TimeSpan.FromMilliseconds(520))
                { BeginTime = delay, EasingFunction = spring });
            st.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.96 - idxOrig * 0.003, 1, TimeSpan.FromMilliseconds(520))
                { BeginTime = delay, EasingFunction = spring });

            idxOrig++;
        }
    }

    // Panel show with spring scale + child stagger
    public static void ShowPanel(Border panel, double slideFromX = 0)
    {
        string mode = GetAnimationLevel();
        if (mode == "None")
        {
            panel.Visibility = Visibility.Visible;
            panel.Opacity = 1;
            panel.RenderTransform = Transform.Identity;
            if (panel.Child is StackPanel spNone)
                StaggerIn(spNone);
            return;
        }
        if (mode == "Reduced")
        {
            panel.Visibility = Visibility.Visible;
            panel.Opacity = 0;
            panel.RenderTransform = Transform.Identity;
            panel.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
                { EasingFunction = AppleSpringEase.Smooth });
            if (panel.Child is StackPanel spReduced)
                StaggerIn(spReduced);
            return;
        }

        panel.Visibility = Visibility.Visible;
        panel.RenderTransformOrigin = new Point(0.5, 0.0);
        var group = new TransformGroup();
        var st = new ScaleTransform(0.94, 0.94);
        var tt = new TranslateTransform(slideFromX, 16);
        group.Children.Add(st);
        group.Children.Add(tt);
        panel.RenderTransform = group;

        var spring = AppleSpringEase.Interactive;
        var smooth = AppleSpringEase.Gentle;
        var springDur = TimeSpan.FromMilliseconds(520);

        panel.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
            { EasingFunction = smooth });
        tt.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(16, 0, springDur) { EasingFunction = spring });
        if (slideFromX != 0)
            tt.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(slideFromX, 0, springDur) { EasingFunction = spring });
        st.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.94, 1, springDur) { EasingFunction = spring });
        st.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.94, 1, springDur) { EasingFunction = spring });

        if (panel.Child is StackPanel sp)
            StaggerIn(sp, baseDelayMs: 40, stepMs: 35);
    }

    // iOS 27 stretchy liquid glass crossfade with bouncy swing-tilt landing
    public static void LiquidGlassCrossfade(Border hidePanel, Border showPanel, double direction)
    {
        if (hidePanel == null || showPanel == null) return;
        if (hidePanel == showPanel) return;

        // Idempotency check: if showPanel is already fully visible and hidePanel is collapsed, do nothing!
        if (showPanel.Visibility == Visibility.Visible && showPanel.Opacity >= 0.95 &&
            (hidePanel.Visibility == Visibility.Collapsed || hidePanel.Opacity <= 0.05))
        {
            return;
        }

        string mode = GetAnimationLevel();
        if (mode == "None")
        {
            hidePanel.Opacity = 0;
            hidePanel.Visibility = Visibility.Collapsed;
            showPanel.Visibility = Visibility.Visible;
            showPanel.Opacity = 1;
            showPanel.RenderTransform = Transform.Identity;
            if (showPanel.Child is StackPanel spNone)
            {
                foreach (UIElement child in spNone.Children)
                {
                    if (child is not FrameworkElement fe) continue;
                    fe.Opacity = 1;
                    fe.RenderTransform = Transform.Identity;
                }
            }
            return;
        }
        if (mode == "Reduced")
        {
            hidePanel.RenderTransform = Transform.Identity;
            var hideOp = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)) { EasingFunction = AppleSpringEase.Smooth };
            hideOp.Completed += (_, _) => hidePanel.Visibility = Visibility.Collapsed;
            hidePanel.BeginAnimation(UIElement.OpacityProperty, hideOp);

            showPanel.Visibility = Visibility.Visible;
            showPanel.RenderTransform = Transform.Identity;
            showPanel.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(150))
                { BeginTime = TimeSpan.FromMilliseconds(60), EasingFunction = AppleSpringEase.Smooth });

            if (showPanel.Child is StackPanel spReduced)
            {
                int idx = 0;
                foreach (UIElement child in spReduced.Children)
                {
                    if (child is not FrameworkElement fe) continue;
                    fe.BeginAnimation(UIElement.OpacityProperty,
                        new DoubleAnimation(1, TimeSpan.FromMilliseconds(150))
                        { BeginTime = TimeSpan.FromMilliseconds(60 + idx * 30), EasingFunction = AppleSpringEase.Smooth });
                    idx++;
                }
            }
            return;
        }

        var springX   = new AppleSpringEase(0.65, 0.44); // elastic X response
        var springY   = new AppleSpringEase(0.70, 0.50); // elastic Y response
        var springRot = new AppleSpringEase(0.58, 0.46); // bouncy swing-tilt response
        var smooth    = AppleSpringEase.Gentle;
        var snappy    = AppleSpringEase.Snappy;

        var hideDur   = TimeSpan.FromMilliseconds(160);
        var showDur   = TimeSpan.FromMilliseconds(500);
        var showDelay = TimeSpan.FromMilliseconds(110);   // overlap timing

        // ── Outgoing panel (fade-out with slide + dynamic tilt) ──
        if (hidePanel.Visibility == Visibility.Visible && hidePanel.Opacity > 0.02)
        {
            hidePanel.RenderTransformOrigin = new Point(0.5, 0.5);
            var hideGroup = new TransformGroup();
            var hideSt = new ScaleTransform(1, 1);
            var hideRt = new RotateTransform(0);
            var hideTt = new TranslateTransform(0, 0);
            hideGroup.Children.Add(hideSt);
            hideGroup.Children.Add(hideRt);
            hideGroup.Children.Add(hideTt);
            hidePanel.RenderTransform = hideGroup;

            var hideOpOrig = new DoubleAnimation(0, hideDur) { EasingFunction = snappy };
            hideOpOrig.Completed += (_, _) => hidePanel.Visibility = Visibility.Collapsed;
            hidePanel.BeginAnimation(UIElement.OpacityProperty, hideOpOrig);

            hideTt.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(0, -direction * 25, hideDur) { EasingFunction = snappy });
            hideSt.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(1, 0.90, hideDur) { EasingFunction = snappy });
            hideSt.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(1, 0.90, hideDur) { EasingFunction = snappy });
            hideRt.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, direction * 3.0, hideDur) { EasingFunction = snappy });
        }
        else
        {
            hidePanel.Visibility = Visibility.Collapsed;
            hidePanel.Opacity = 0;
        }

        // ── Incoming panel (stretchy landing + bouncy rotation) ──
        bool isAlreadyShowing = showPanel.Visibility == Visibility.Visible && showPanel.Opacity > 0.5;
        showPanel.Visibility = Visibility.Visible;
        if (!isAlreadyShowing) showPanel.Opacity = 0;

        showPanel.RenderTransformOrigin = new Point(0.5, 0.5);
        var showGroup = new TransformGroup();
        var showSt = new ScaleTransform(0.88, 0.84); // vertical starting stretch
        var showRt = new RotateTransform(-direction * 4.0); // start tilt
        var showTt = new TranslateTransform(direction * 35, 10); // start slide offset
        showGroup.Children.Add(showSt);
        showGroup.Children.Add(showRt);
        showGroup.Children.Add(showTt);
        showPanel.RenderTransform = showGroup;

        showPanel.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(260))
            { BeginTime = showDelay, EasingFunction = smooth });

        showTt.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(direction * 35, 0, showDur)
            { BeginTime = showDelay, EasingFunction = springX });
        showTt.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, showDur)
            { BeginTime = showDelay, EasingFunction = springY });

        showSt.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.88, 1.0, showDur)
            { BeginTime = showDelay, EasingFunction = springX });
        showSt.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.84, 1.0, showDur)
            { BeginTime = showDelay, EasingFunction = springY });

        showRt.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(-direction * 4.0, 0, showDur)
            { BeginTime = showDelay, EasingFunction = springRot });

        // ── Apple Glassmorphic Staggered Child Blossom (Flicker-Free) ──
        StackPanel? sp = showPanel.Child as StackPanel;
        if (sp != null)
        {
            int idx = 0;
            foreach (UIElement child in sp.Children)
            {
                if (child is not FrameworkElement fe) continue;

                if (!isAlreadyShowing) fe.Opacity = 0;
                fe.RenderTransformOrigin = new Point(0.5, 0.5);
                var childGroup = new TransformGroup();
                var childScale = new ScaleTransform(0.92, 0.92);
                var childTrans = new TranslateTransform(direction * 14, 12 + idx * 2);
                childGroup.Children.Add(childScale);
                childGroup.Children.Add(childTrans);
                fe.RenderTransform = childGroup;

                var childDur = TimeSpan.FromMilliseconds(260 + idx * 35);
                var childSpring = new AppleSpringEase(0.72, 0.44 + idx * 0.02);

                fe.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220 + idx * 25))
                    { EasingFunction = smooth });

                childTrans.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(direction * 14, 0, childDur)
                    { EasingFunction = childSpring });
                childTrans.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(12 + idx * 2, 0, childDur)
                    { EasingFunction = childSpring });

                childScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(0.92, 1.0, childDur)
                    { EasingFunction = childSpring });
                childScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(0.92, 1.0, childDur)
                    { EasingFunction = childSpring });

                idx++;
            }
        }
    }

    // Panel hide with snappy ease
    public static void HidePanel(Border panel, double slideX = 0, Action? onCompleted = null)
    {
        string mode = GetAnimationLevel();
        if (mode == "None")
        {
            panel.Visibility = Visibility.Collapsed;
            onCompleted?.Invoke();
            return;
        }
        if (mode == "Reduced")
        {
            panel.RenderTransform = Transform.Identity;
            var opAnim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150)) { EasingFunction = AppleSpringEase.Smooth };
            opAnim.Completed += (_, _) =>
            {
                panel.Visibility = Visibility.Collapsed;
                onCompleted?.Invoke();
            };
            panel.BeginAnimation(UIElement.OpacityProperty, opAnim);
            return;
        }

        var ease = AppleSpringEase.Snappy;
        var dur = TimeSpan.FromMilliseconds(200);

        panel.RenderTransformOrigin = new Point(0.5, 0.0);
        var group = new TransformGroup();
        var st = new ScaleTransform(1, 1);
        var tt = new TranslateTransform(0, 0);
        group.Children.Add(st);
        group.Children.Add(tt);
        panel.RenderTransform = group;

        var opAnimOrig = new DoubleAnimation(1, 0, dur) { EasingFunction = ease };
        opAnimOrig.Completed += (_, _) =>
        {
            panel.Visibility = Visibility.Collapsed;
            onCompleted?.Invoke();
        };

        panel.BeginAnimation(UIElement.OpacityProperty, opAnimOrig);
        tt.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, 6, dur) { EasingFunction = ease });
        if (slideX != 0)
            tt.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(0, slideX, dur) { EasingFunction = ease });
        st.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, 0.97, dur) { EasingFunction = ease });
        st.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, 0.97, dur) { EasingFunction = ease });
    }

    // Progress bar spring animation
    public static void AnimateProgressWidth(FrameworkElement fill, double targetWidth)
    {
        var theme = GetThemeSettings();
        string preset = theme.AnimationPreset ?? theme.AnimationLevel ?? "Balanced";
        if (preset == "Disabled (Static)" || preset == "Disabled" || preset == "None")
        {
            fill.BeginAnimation(FrameworkElement.WidthProperty, null);
            fill.Width = targetWidth;
            return;
        }

        int speedMs = theme.ProgressBarAnimSpeed > 0 ? theme.ProgressBarAnimSpeed : 350;
        fill.BeginAnimation(FrameworkElement.WidthProperty,
            new DoubleAnimation(targetWidth, TimeSpan.FromMilliseconds(speedMs))
            {
                EasingFunction = AppleSpringEase.Smooth
            });
    }

    // Phase transition out (progress rows)
    public static void PhaseOut(FrameworkElement[] rows, Action onComplete)
    {
        string mode = GetAnimationLevel();
        if (mode == "None")
        {
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i].Opacity = 0;
            }
            onComplete();
            return;
        }
        if (mode == "Reduced")
        {
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                row.RenderTransform = Transform.Identity;
                var delay = TimeSpan.FromMilliseconds(i * 15);
                var opAnim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120)) { EasingFunction = AppleSpringEase.Smooth, BeginTime = delay };
                if (i == rows.Length - 1)
                    opAnim.Completed += (_, _) => onComplete();
                row.BeginAnimation(UIElement.OpacityProperty, opAnim);
            }
            return;
        }

        var ease = AppleSpringEase.Snappy;

        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            EnsureTranslateScale(row);
            var group = (TransformGroup)row.RenderTransform;
            var st = (ScaleTransform)group.Children[0];
            var tt = (TranslateTransform)group.Children[1];
            var delay = TimeSpan.FromMilliseconds(i * 25);
            var dur = TimeSpan.FromMilliseconds(160);

            var opAnim = new DoubleAnimation(1, 0, dur)
            { EasingFunction = ease, BeginTime = delay };
            var yAnim = new DoubleAnimation(0, 5, dur)
            { EasingFunction = ease, BeginTime = delay };
            var sAnim = new DoubleAnimation(1, 0.97, dur)
            { EasingFunction = ease, BeginTime = delay };

            if (i == rows.Length - 1)
                opAnim.Completed += (_, _) => onComplete();

            row.BeginAnimation(UIElement.OpacityProperty, opAnim);
            tt.BeginAnimation(TranslateTransform.YProperty, yAnim);
            st.BeginAnimation(ScaleTransform.ScaleXProperty, sAnim);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, sAnim);
        }
    }

    // Phase transition in (progress rows)
    public static void PhaseIn(FrameworkElement[] rows, Action? onComplete = null)
    {
        string mode = GetAnimationLevel();
        if (mode == "None")
        {
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i].Opacity = 1;
                rows[i].RenderTransform = Transform.Identity;
            }
            onComplete?.Invoke();
            return;
        }
        if (mode == "Reduced")
        {
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                row.RenderTransform = Transform.Identity;
                var delay = TimeSpan.FromMilliseconds(i * 20);
                row.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)) { EasingFunction = AppleSpringEase.Smooth, BeginTime = delay });
            }
            if (onComplete != null)
            {
                var totalMs = (rows.Length - 1) * 20 + 150;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(totalMs) };
                timer.Tick += (_, _) => { timer.Stop(); onComplete(); };
                timer.Start();
            }
            return;
        }

        var spring = AppleSpringEase.Interactive;
        var smooth = AppleSpringEase.Gentle;

        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            var group = (TransformGroup)row.RenderTransform;
            var st = (ScaleTransform)group.Children[0];
            var tt = (TranslateTransform)group.Children[1];
            var delay = TimeSpan.FromMilliseconds(i * 45);

            row.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
                { BeginTime = delay, EasingFunction = smooth });
            tt.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(440))
                { BeginTime = delay, EasingFunction = spring });
            st.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(440))
                { BeginTime = delay, EasingFunction = spring });
            st.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(440))
                { BeginTime = delay, EasingFunction = spring });
        }

        if (onComplete != null)
        {
            var totalMs = (rows.Length - 1) * 45 + 440;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(totalMs) };
            timer.Tick += (_, _) => { timer.Stop(); onComplete(); };
            timer.Start();
        }
    }

    // Log panel expand
    public static void ExpandElement(FrameworkElement element, double targetMaxHeight)
    {
        string mode = GetAnimationLevel();
        if (mode == "None")
        {
            element.BeginAnimation(FrameworkElement.MaxHeightProperty, null);
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.MaxHeight = targetMaxHeight;
            element.Opacity = 1;
            element.Visibility = Visibility.Visible;
            return;
        }
        if (mode == "Reduced")
        {
            element.BeginAnimation(FrameworkElement.MaxHeightProperty, null);
            element.MaxHeight = targetMaxHeight;
            element.Opacity = 0;
            element.Visibility = Visibility.Visible;
            element.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)) { EasingFunction = AppleSpringEase.Smooth });
            return;
        }

        var spring = AppleSpringEase.Interactive;
        var smooth = AppleSpringEase.Gentle;

        element.BeginAnimation(FrameworkElement.MaxHeightProperty, null);
        element.MaxHeight = 0;
        element.Opacity = 0;
        element.Visibility = Visibility.Visible;

        element.BeginAnimation(FrameworkElement.MaxHeightProperty,
            new DoubleAnimation(0, targetMaxHeight, TimeSpan.FromMilliseconds(380))
            { EasingFunction = spring });
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240))
            { EasingFunction = smooth });
    }

    // Log panel collapse
    public static void CollapseElement(FrameworkElement element, double currentHeight, Action onCompleted)
    {
        string mode = GetAnimationLevel();
        if (mode == "None")
        {
            element.Visibility = Visibility.Collapsed;
            element.BeginAnimation(FrameworkElement.MaxHeightProperty, null);
            element.MaxHeight = currentHeight;
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            onCompleted();
            return;
        }
        if (mode == "Reduced")
        {
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150)) { EasingFunction = AppleSpringEase.Smooth };
            fade.Completed += (_, _) =>
            {
                element.Visibility = Visibility.Collapsed;
                element.BeginAnimation(FrameworkElement.MaxHeightProperty, null);
                element.MaxHeight = currentHeight;
                element.BeginAnimation(UIElement.OpacityProperty, null);
                element.Opacity = 1;
                onCompleted();
            };
            element.BeginAnimation(UIElement.OpacityProperty, fade);
            return;
        }

        var ease = AppleSpringEase.Snappy;

        var heightAnim = new DoubleAnimation(currentHeight, 0, TimeSpan.FromMilliseconds(220))
        { EasingFunction = ease };
        heightAnim.Completed += (_, _) =>
        {
            element.Visibility = Visibility.Collapsed;
            element.BeginAnimation(FrameworkElement.MaxHeightProperty, null);
            element.MaxHeight = currentHeight;
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            onCompleted();
        };
        element.BeginAnimation(FrameworkElement.MaxHeightProperty, heightAnim);
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
    }

    // Element reveal (detected-platform badge, etc.)
    public static void RevealElement(FrameworkElement element)
    {
        string mode = GetAnimationLevel();
        if (mode == "None")
        {
            element.Visibility = Visibility.Visible;
            element.Opacity = 1;
            element.RenderTransform = Transform.Identity;
            return;
        }
        if (mode == "Reduced")
        {
            element.Visibility = Visibility.Visible;
            element.Opacity = 0;
            element.RenderTransform = Transform.Identity;
            element.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)) { EasingFunction = AppleSpringEase.Smooth });
            return;
        }

        element.Visibility = Visibility.Visible;
        element.Opacity = 0;
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        var st = new ScaleTransform(0.90, 0.90);
        element.RenderTransform = st;

        var spring = AppleSpringEase.Bouncy;
        var smooth = AppleSpringEase.Gentle;

        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240))
            { EasingFunction = smooth });
        st.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.90, 1, TimeSpan.FromMilliseconds(420))
            { EasingFunction = spring });
        st.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.90, 1, TimeSpan.FromMilliseconds(420))
            { EasingFunction = spring });
    }

    // Element dismiss (fade and shrink out)
    public static void DismissElement(FrameworkElement element, Action? onCompleted = null)
    {
        if (element.Visibility != Visibility.Visible)
        {
            onCompleted?.Invoke();
            return;
        }

        string mode = GetAnimationLevel();
        if (mode == "None")
        {
            element.Visibility = Visibility.Collapsed;
            onCompleted?.Invoke();
            return;
        }
        if (mode == "Reduced")
        {
            var opAnim = new DoubleAnimation(element.Opacity, 0, TimeSpan.FromMilliseconds(150)) { EasingFunction = AppleSpringEase.Smooth };
            opAnim.Completed += (_, _) =>
            {
                element.Visibility = Visibility.Collapsed;
                onCompleted?.Invoke();
            };
            element.BeginAnimation(UIElement.OpacityProperty, opAnim);
            return;
        }

        var ease = AppleSpringEase.Snappy;
        var dur = TimeSpan.FromMilliseconds(200);

        var st = element.RenderTransform as ScaleTransform ?? new ScaleTransform(1, 1);
        element.RenderTransform = st;

        var opAnimOrig = new DoubleAnimation(element.Opacity, 0, dur) { EasingFunction = ease };
        opAnimOrig.Completed += (_, _) =>
        {
            element.Visibility = Visibility.Collapsed;
            onCompleted?.Invoke();
        };

        element.BeginAnimation(UIElement.OpacityProperty, opAnimOrig);
        st.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(st.ScaleX, 0.90, dur) { EasingFunction = ease });
        st.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(st.ScaleY, 0.90, dur) { EasingFunction = ease });
    }

    // Dev-console body show
    public static void ShowBody(FrameworkElement body, RowDefinition row, double height)
    {
        string mode = GetAnimationLevel();
        if (mode == "None" || mode == "Reduced")
        {
            body.Visibility = Visibility.Visible;
            row.Height = new GridLength(height);
            body.Opacity = 1;
            return;
        }

        var smooth = AppleSpringEase.Gentle;
        body.Visibility = Visibility.Visible;
        row.Height = new GridLength(height);
        body.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
            { EasingFunction = smooth });
    }

    // Dev-console body hide
    public static void HideBody(FrameworkElement body, RowDefinition row, Action onDone)
    {
        string mode = GetAnimationLevel();
        if (mode == "None" || mode == "Reduced")
        {
            body.Visibility = Visibility.Collapsed;
            row.Height = new GridLength(0);
            onDone();
            return;
        }

        var ease = AppleSpringEase.Snappy;
        var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180))
        { EasingFunction = ease };
        anim.Completed += (_, _) =>
        {
            body.Visibility = Visibility.Collapsed;
            row.Height = new GridLength(0);
            onDone();
        };
        body.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    // Status dot color animation
    public static void AnimateColor(SolidColorBrush brush, Color target)
    {
        string mode = GetAnimationLevel();
        if (mode == "None")
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            brush.Color = target;
            return;
        }

        brush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(target, TimeSpan.FromMilliseconds(350))
            { EasingFunction = AppleSpringEase.Smooth });
    }

    public static void AnimateGradientStop(GradientStop stop, Color target)
    {
        string mode = GetAnimationLevel();
        if (mode == "None")
        {
            stop.BeginAnimation(GradientStop.ColorProperty, null);
            stop.Color = target;
            return;
        }

        stop.BeginAnimation(GradientStop.ColorProperty,
            new ColorAnimation(target, TimeSpan.FromMilliseconds(350))
            { EasingFunction = AppleSpringEase.Smooth });
    }

    // ── Animatable CornerRadius for liquid glass shape morphing ──
    // WPF has no built-in CornerRadiusAnimation, so we proxy through an
    // attached double that pushes a uniform CornerRadius on every change.

    public static readonly DependencyProperty CornerRadiusValueProperty =
        DependencyProperty.RegisterAttached(
            "CornerRadiusValue",
            typeof(double),
            typeof(FluidMotion),
            new PropertyMetadata(0.0, OnCornerRadiusValueChanged));

    public static double GetCornerRadiusValue(DependencyObject d) =>
        (double)d.GetValue(CornerRadiusValueProperty);

    public static void SetCornerRadiusValue(DependencyObject d, double value) =>
        d.SetValue(CornerRadiusValueProperty, value);

    private static void OnCornerRadiusValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Border b)
        {
            double r = (double)e.NewValue;
            b.CornerRadius = new CornerRadius(r);
        }
    }

    public static void AnimateCornerRadius(Border border, double to, TimeSpan duration,
        IEasingFunction? ease = null, TimeSpan? beginTime = null)
    {
        string mode = GetAnimationLevel();
        if (mode == "None")
        {
            border.BeginAnimation(CornerRadiusValueProperty, null);
            SetCornerRadiusImmediate(border, to);
            return;
        }

        var anim = new DoubleAnimation(to, duration);
        if (ease != null) anim.EasingFunction = ease;
        if (beginTime.HasValue) anim.BeginTime = beginTime.Value;
        border.BeginAnimation(CornerRadiusValueProperty, anim);
    }

    public static void AnimateCornerRadiusKeyFrames(Border border,
        DoubleAnimationUsingKeyFrames anim)
    {
        border.BeginAnimation(CornerRadiusValueProperty, anim);
    }

    public static void SetCornerRadiusImmediate(Border border, double value)
    {
        border.BeginAnimation(CornerRadiusValueProperty, null);
        SetCornerRadiusValue(border, value);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static void EnsureTranslateScale(FrameworkElement element)
    {
        if (element.RenderTransform is TransformGroup tg && tg.Children.Count >= 2) return;
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(1, 1));
        group.Children.Add(new TranslateTransform(0, 0));
        element.RenderTransform = group;
    }
}
