using System.Windows;
using System.Windows.Input;

namespace ReelsConverterUI;

public partial class DescriptionEditorWindow : Window
{
    public string Description { get; private set; } = string.Empty;

    public DescriptionEditorWindow(string initialText)
    {
        InitializeComponent();
        TxtEditor.Text = initialText;
        TxtEditor.CaretIndex = TxtEditor.Text.Length;
        Loaded += (_, _) => TxtEditor.Focus();
    }

    private void TitleBar_Drag(object s, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Apply_Click(object s, RoutedEventArgs e)
    {
        Description = TxtEditor.Text;
        DialogResult = true;
    }

    private void Cancel_Click(object s, RoutedEventArgs e) => DialogResult = false;
    private void Close_Click(object s, RoutedEventArgs e) => DialogResult = false;
}
