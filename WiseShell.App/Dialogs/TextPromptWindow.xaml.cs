using System.Windows;

namespace WiseShell.App.Dialogs;

public partial class TextPromptWindow : Window
{
    public TextPromptWindow(string title, string message, string initialValue = "")
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = message;
        ValueTextBox.Text = initialValue;
        ValueTextBox.SelectAll();
    }

    public string Value => ValueTextBox.Text;

    private void Confirm_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
