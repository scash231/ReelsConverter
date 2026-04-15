using ReelsConverterUI.Animations;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ReelsConverterUI;

public partial class DescriptionEditorWindow : Window
{
    public string Description { get; private set; } = string.Empty;
    private readonly Rect _originRect;
    private bool _isAnimatingClose;
    private bool? _pendingResult;

    public DescriptionEditorWindow(string initialText, Rect originRect)
    {
        InitializeComponent();
        _originRect = originRect;
        TxtEditor.Text = initialText;
        TxtEditor.CaretIndex = TxtEditor.Text.Length;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FluidMotion.MorphOpen(RootBorder, PopScale, PopTranslate, _originRect, this);
        TxtEditor.Focus();
    }

    private void TitleBar_Drag(object s, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Apply_Click(object s, RoutedEventArgs e) => CloseWithAnimation(true);
    private void Cancel_Click(object s, RoutedEventArgs e) => CloseWithAnimation(false);
    private void Close_Click(object s, RoutedEventArgs e) => CloseWithAnimation(false);

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isAnimatingClose) { e.Cancel = true; CloseWithAnimation(false); }
        base.OnClosing(e);
    }

    private void CloseWithAnimation(bool? result)
    {
        if (_isAnimatingClose) return;
        _isAnimatingClose = true;
        if (result == true) Description = TxtEditor.Text;
        _pendingResult = result;
        FluidMotion.MorphClose(RootBorder, PopScale, PopTranslate, _originRect, this,
            () => DialogResult = _pendingResult);
    }
}
