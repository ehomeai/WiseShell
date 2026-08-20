using System.Windows;
using WiseShell.App.Infrastructure;
using WiseShell.App.ViewModels;

namespace WiseShell.App.Dialogs;

public partial class TerminalDefaultsWindow : Window
{
    public TerminalDefaultsWindow(TerminalDefaultsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        FontFamilyComboBox.ItemsSource = TerminalFontCatalog.GetRecommendedFontFamilies();
    }

    public TerminalDefaultsViewModel ViewModel => (TerminalDefaultsViewModel)DataContext;

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.FontSize < 9d || ViewModel.FontSize > 36d)
        {
            MessageBox.Show(this, "默认字号请设置在 9 到 36 之间。", "设置无效", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(ViewModel.FontFamily))
        {
            MessageBox.Show(this, "默认字体不能为空。", "设置无效", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ViewModel.FontFamily = TerminalFontCatalog.NormalizeFontFamily(ViewModel.FontFamily);
        DialogResult = true;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
