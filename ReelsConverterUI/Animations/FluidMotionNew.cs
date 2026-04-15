using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

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
    // Window morph open (iOS 26: scale from button position with spring)
    public static void MorphOpen(
        Border root,
        ScaleTransform scale,
        TranslateTransform translate,
        Rect origin,
        Window window)
    {
        var winW = window.ActualWidth;
        var winH = window.ActualHeight;
        var btnCx = origin.X + origin.Width / 2;
        var btnCy = origin.Y + origin.Height / 2;

        // Dynamic pivot: button center relative to window (0-1)
        double ox = Math.Clamp((btnCx - window.Left) / winW, 0.0, 1.0);
        double oy = Math.Clamp((btnCy - window.Top) / winH, 0.0, 1.0);
        root.RenderTransformOrigin = new Point(ox, oy);

        // Starting scale = actual button/window ratio
        double sx = Math.Clamp(origin.Width / winW, 0.03, 0.45);
        double sy = Math.Clamp(origin.Height / winH, 0.03, 0.45);

        // Translate corrects offset when button is outside window bounds
        double tx = btnCx - window.Left - ox * winW;
        double ty = btnCy - window.Top - oy * winH;

        var spring = AppleSpringEase.Interactive;
        var smooth = AppleSpringEase.Smooth;
        var springDur = TimeSpan.FromMilliseconds(600);
        var ySpringDur = TimeSpan.FromMilliseconds(640);
        var fadeDur = TimeSpan.FromMilliseconds(220);
        var yDelay = TimeSpan.FromMilliseconds(35);

        root.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, fadeDur) { EasingFunction = smooth });

        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(sx, 1, springDur) { EasingFunction = spring });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(sy, 1, ySpringDur)
            { BeginTime = yDelay, EasingFunction = spring });

        translate.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(tx, 0, springDur) { EasingFunction = smooth });
        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(ty, 0, ySpringDur)
            { BeginTime = yDelay, EasingFunction = smooth });
    }

    // Window morph close (shrink back toward button, fast fade)
    public static void MorphClose(
        Border root,
        ScaleTransform scale,
        TranslateTransform translate,
        Rect origin,
        Window window,
        Action onCompleted)
    {
        var winW = window.ActualWidth;
        var winH = window.ActualHeight;
        var btnCx = origin.X + origin.Width / 2;
        var btnCy = origin.Y + origin.Height / 2;

        // Same dynamic pivot as open
        double ox = Math.Clamp((btnCx - window.Left) / winW, 0.0, 1.0);
        double oy = Math.Clamp((btnCy - window.Top) / winH, 0.0, 1.0);
        root.RenderTransformOrigin = new Point(ox, oy);

        // Shrink ~60% back toward button size
        double rawSx = Math.Clamp(origin.Width / winW, 0.03, 0.45);
        double rawSy = Math.Clamp(origin.Height / winH, 0.03, 0.45);
        double targetSx = Lerp(1.0, rawSx, 0.6);
        double targetSy = Lerp(1.0, rawSy, 0.6);

        // Translate back partially toward button
        double fullTx = btnCx - window.Left - ox * winW;
        double fullTy = btnCy - window.Top - oy * winH;
        double targetTx = fullTx * 0.6;
        double targetTy = fullTy * 0.6;

        var ease = AppleSpringEase.Snappy;
        var dur = TimeSpan.FromMilliseconds(250);

        var opAnim = new DoubleAnimation(1, 0, dur) { EasingFunction = ease };
        opAnim.Completed += (_, _) => onCompleted();

        root.BeginAnimation(UIElement.OpacityProperty, opAnim);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, targetSx, dur) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, targetSy, dur) { EasingFunction = ease });
        translate.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(0, targetTx, dur) { EasingFunction = ease });
        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, targetTy, dur) { EasingFunction = ease });
    }

    // Stagger children with depth-aware spring (travel increases per child)
    public static void StaggerIn(Panel panel, int baseDelayMs = 60, int stepMs = 50)
    {
        var spring = AppleSpringEase.Bouncy;
        var smooth = AppleSpringEase.Gentle;
        int idx = 0;

        foreach (UIElement child in panel.Children)
        {
            if (child is not FrameworkElement fe) continue;

            fe.Opacity = 0;
            fe.RenderTransformOrigin = new Point(0.5, 0.0);
            var group = new TransformGroup();
            var st = new ScaleTransform(0.96 - idx * 0.003, 0.96 - idx * 0.003);
            var tt = new TranslateTransform(0, 20 + idx * 3);
            group.Children.Add(st);
            group.Children.Add(tt);
            fe.RenderTransform = group;

            var delay = TimeSpan.FromMilliseconds(baseDelayMs + idx * stepMs);

            fe.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
                { BeginTime = delay, EasingFunction = smooth });

            tt.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(20 + idx * 3, 0, TimeSpan.FromMilliseconds(520))
                { BeginTime = delay, EasingFunction = spring });

            st.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.96 - idx * 0.003, 1, TimeSpan.FromMilliseconds(520))
                { BeginTime = delay, EasingFunction = spring });
            st.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.96 - idx * 0.003, 1, TimeSpan.FromMilliseconds(520))
                { BeginTime = delay, EasingFunction = spring });

            idx++;
        }
    }

    // Panel show with spring scale + child stagger
    public static void ShowPanel(Border panel, double slideFromX = 0)
    {
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

    // iOS 26 liquid glass crossfade between two panels
    // Synced with pill: outgoing fades immediately, incoming arrives with the pill slide (~120ms delay)
    public static void LiquidGlassCrossfade(Border hidePanel, Border showPanel, double direction)
    {
        var spring  = new AppleSpringEase(0.72, 0.48);
        var smooth  = AppleSpringEase.Gentle;
        var snappy  = AppleSpringEase.Snappy;

        var hideDur   = TimeSpan.FromMilliseconds(180);
        var showDur   = TimeSpan.FromMilliseconds(460);
        var showDelay = TimeSpan.FromMilliseconds(120);   // aligned with pill slide start

        // ── Outgoing panel ──
        hidePanel.RenderTransformOrigin = new Point(0.5, 0.5);
        var hideGroup = new TransformGroup();
        var hideSt = new ScaleTransform(1, 1);
        var hideTt = new TranslateTransform(0, 0);
        hideGroup.Children.Add(hideSt);
        hideGroup.Children.Add(hideTt);
        hidePanel.RenderTransform = hideGroup;

        var hideOp = new DoubleAnimation(1, 0, hideDur) { EasingFunction = snappy };
        hideOp.Completed += (_, _) => hidePanel.Visibility = Visibility.Collapsed;
        hidePanel.BeginAnimation(UIElement.OpacityProperty, hideOp);
        hideTt.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(0, -direction * 20, hideDur) { EasingFunction = snappy });
        hideSt.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, 0.95, hideDur) { EasingFunction = snappy });
        hideSt.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, 0.95, hideDur) { EasingFunction = snappy });

        // ── Incoming panel ──
        showPanel.Visibility = Visibility.Visible;
        showPanel.Opacity = 0;
        showPanel.RenderTransformOrigin = new Point(0.5, 0.5);
        var showGroup = new TransformGroup();
        var showSt = new ScaleTransform(0.93, 0.93);
        var showTt = new TranslateTransform(direction * 28, 0);
        showGroup.Children.Add(showSt);
        showGroup.Children.Add(showTt);
        showPanel.RenderTransform = showGroup;

        showPanel.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240))
            { BeginTime = showDelay, EasingFunction = smooth });
        showTt.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(direction * 28, 0, showDur)
            { BeginTime = showDelay, EasingFunction = spring });
        showSt.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.93, 1, showDur)
            { BeginTime = showDelay, EasingFunction = spring });
        showSt.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.93, 1, showDur)
            { BeginTime = showDelay, EasingFunction = spring });

        // Stagger children in slide direction
        if (showPanel.Child is StackPanel sp)
        {
            var staggerSpring = AppleSpringEase.Bouncy;
            int idx = 0;
            foreach (UIElement child in sp.Children)
            {
                if (child is not FrameworkElement fe) continue;
                fe.Opacity = 0;
                fe.RenderTransformOrigin = new Point(0.5, 0.5);
                double travel = direction * (10 + idx * 3);
                var tt = new TranslateTransform(travel, 0);
                fe.RenderTransform = tt;

                var delay = TimeSpan.FromMilliseconds(
                    showDelay.TotalMilliseconds + 30 + idx * 35);
                fe.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
                    { BeginTime = delay, EasingFunction = smooth });
                tt.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(travel, 0, TimeSpan.FromMilliseconds(400))
                    { BeginTime = delay, EasingFunction = staggerSpring });
                idx++;
            }
        }
    }

    // Panel hide with snappy ease
    public static void HidePanel(Border panel, double slideX = 0, Action? onCompleted = null)
    {
        var ease = AppleSpringEase.Snappy;
        var dur = TimeSpan.FromMilliseconds(200);

        panel.RenderTransformOrigin = new Point(0.5, 0.0);
        var group = new TransformGroup();
        var st = new ScaleTransform(1, 1);
        var tt = new TranslateTransform(0, 0);
        group.Children.Add(st);
        group.Children.Add(tt);
        panel.RenderTransform = group;

        var opAnim = new DoubleAnimation(1, 0, dur) { EasingFunction = ease };
        opAnim.Completed += (_, _) =>
        {
            panel.Visibility = Visibility.Collapsed;
            onCompleted?.Invoke();
        };

        panel.BeginAnimation(UIElement.OpacityProperty, opAnim);
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
        fill.BeginAnimation(FrameworkElement.WidthProperty,
            new DoubleAnimation(targetWidth, TimeSpan.FromMilliseconds(400))
            {
                EasingFunction = AppleSpringEase.Smooth
            });
    }

    // Phase transition out (progress rows)
    public static void PhaseOut(FrameworkElement[] rows, Action onComplete)
    {
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

    // Dev-console body show
    public static void ShowBody(FrameworkElement body, RowDefinition row, double height)
    {
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
        brush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(target, TimeSpan.FromMilliseconds(350))
            { EasingFunction = AppleSpringEase.Smooth });
    }

    public static void AnimateGradientStop(GradientStop stop, Color target)
    {
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
