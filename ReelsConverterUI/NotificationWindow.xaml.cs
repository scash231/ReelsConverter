using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ReelsConverterUI.Animations;

namespace ReelsConverterUI;

public enum NotificationType
{
    Info,
    Warning,
    Error
}

public partial class NotificationWindow : Window
{
    private static NotificationWindow? _activeNotification;
    
    private readonly DispatcherTimer _timer;
    private bool _isClosing;
    private readonly Window? _parentWindow;

    public NotificationWindow(string message, NotificationType type, Window? parentWindow = null)
    {
        InitializeComponent();
        
        _parentWindow = parentWindow ?? Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;
        TxtMessage.Text = message;
        
        // Setup visual styling based on notification type
        SetupStyle(type);
        
        // Configure auto-dismiss timer
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(4.2)
        };
        _timer.Tick += (s, e) => Dismiss();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Position relative to parent window
        UpdatePosition();
        
        // Start auto-dismiss timer
        _timer.Start();

        // Animate entrance
        AnimateIn();
    }

    private void UpdatePosition()
    {
        if (_parentWindow != null && _parentWindow.IsVisible)
        {
            // Position at top-left of parent window
            Left = _parentWindow.Left + 16;
            Top = _parentWindow.Top + 44;
        }
        else
        {
            // Position at top-left of work area
            Left = SystemParameters.WorkArea.Left + 16;
            Top = SystemParameters.WorkArea.Top + 44;
        }
    }

    private void SetupStyle(NotificationType type)
    {
        // Resolve glass background matching theme dynamic resources
        var cardBrush = Application.Current.TryFindResource("BgCard") as SolidColorBrush;
        if (cardBrush != null)
        {
            var c = cardBrush.Color;
            NotificationBorder.Background = new SolidColorBrush(Color.FromArgb(0xD8, c.R, c.G, c.B));
        }
        else
        {
            NotificationBorder.Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x20, 0x20, 0x24));
        }

        // Apply visual accents based on type
        switch (type)
        {
            case NotificationType.Error:
                TxtIcon.Text = "✕";
                var errBrush = Application.Current.TryFindResource("ErrorRed") as SolidColorBrush;
                if (errBrush != null)
                {
                    TxtIcon.Foreground = errBrush;
                    NotificationBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, errBrush.Color.R, errBrush.Color.G, errBrush.Color.B));
                }
                else
                {
                    TxtIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xC4, 0x48, 0x48));
                    NotificationBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xC4, 0x48, 0x48));
                }
                break;
                
            case NotificationType.Info:
                TxtIcon.Text = "ℹ";
                var succBrush = Application.Current.TryFindResource("SuccessGreen") as SolidColorBrush;
                if (succBrush != null)
                {
                    TxtIcon.Foreground = succBrush;
                    NotificationBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, succBrush.Color.R, succBrush.Color.G, succBrush.Color.B));
                }
                else
                {
                    TxtIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0xAF, 0x6E));
                    NotificationBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x5A, 0xAF, 0x6E));
                }
                break;
                
            case NotificationType.Warning:
            default:
                TxtIcon.Text = "⚠";
                var warnBrush = Application.Current.TryFindResource("Accent") as SolidColorBrush;
                if (warnBrush != null)
                {
                    TxtIcon.Foreground = warnBrush;
                    NotificationBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, warnBrush.Color.R, warnBrush.Color.G, warnBrush.Color.B));
                }
                else
                {
                    TxtIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x9E, 0xC0));
                    NotificationBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x7A, 0x9E, 0xC0));
                }
                break;
        }
    }

    private void AnimateIn()
    {
        var spring = AppleSpringEase.Interactive;
        var smooth = AppleSpringEase.Gentle;
        
        Opacity = 0;
        
        BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            { EasingFunction = smooth });
            
        WindowScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(450))
            { EasingFunction = spring });
            
        WindowScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(450))
            { EasingFunction = spring });
            
        WindowTranslate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(-16, 0, TimeSpan.FromMilliseconds(450))
            { EasingFunction = spring });
    }

    public void Dismiss()
    {
        if (_isClosing) return;
        _isClosing = true;
        _timer.Stop();

        var ease = AppleSpringEase.Snappy;
        var dur = TimeSpan.FromMilliseconds(200);

        var opAnim = new DoubleAnimation(1, 0, dur) { EasingFunction = ease };
        opAnim.Completed += (s, e) => Close();

        BeginAnimation(OpacityProperty, opAnim);
        
        WindowTranslate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, -16, dur) { EasingFunction = ease });
            
        WindowScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, 0.92, dur) { EasingFunction = ease });
            
        WindowScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, 0.92, dur) { EasingFunction = ease });
            
        if (_activeNotification == this)
        {
            _activeNotification = null;
        }
    }

    private void Notification_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            Dismiss();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Dismiss();
        e.Handled = true;
    }

    public static void Show(string message, Window? owner = null, NotificationType type = NotificationType.Warning)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_activeNotification != null)
            {
                try { _activeNotification.Close(); } catch { }
            }
            
            var win = new NotificationWindow(message, type, owner);
            _activeNotification = win;
            win.Show();
        });
    }
}
