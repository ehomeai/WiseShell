using System.Windows;
using WiseShell.Core.Models;

namespace WiseShell.App.Dialogs;

public partial class HostKeyPromptWindow : Window
{
    public HostKeyPromptWindow(HostKeyVerificationRequest request)
    {
        InitializeComponent();
        DataContext = request;
    }

    private void Accept_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Reject_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
