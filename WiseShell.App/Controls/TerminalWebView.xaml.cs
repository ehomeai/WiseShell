using System.IO;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using WiseShell.App.ViewModels;
using WiseShell.Core.Enums;
using WiseShell.Core.Models;

namespace WiseShell.App.Controls;

public partial class TerminalWebView : UserControl
{
    private TerminalTabViewModel? _viewModel;
    private bool _isInitialized;
    private bool _documentReady;
    private bool _isBufferSynchronized;
    private int _renderedBufferLength;

    public TerminalWebView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await EnsureInitializedAsync();
        AttachViewModel(DataContext as TerminalTabViewModel);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachViewModel();
        _isBufferSynchronized = false;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        AttachViewModel(e.NewValue as TerminalTabViewModel);
    }

    private void AttachViewModel(TerminalTabViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            if (_viewModel is not null && _documentReady && !_isBufferSynchronized)
            {
                _ = ApplyViewModelStateAsync();
            }

            return;
        }

        DetachViewModel();
        _viewModel = viewModel;
        if (_viewModel is null)
        {
            _renderedBufferLength = 0;
            _isBufferSynchronized = false;
            return;
        }

        _renderedBufferLength = 0;
        _isBufferSynchronized = false;
        _viewModel.OutputAppended += OnOutputAppended;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        if (_documentReady)
        {
            _ = ApplyViewModelStateAsync();
        }
    }

    private void DetachViewModel()
    {
        if (_viewModel is not null)
        {
            _viewModel.OutputAppended -= OnOutputAppended;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_documentReady || _viewModel is null)
        {
            return;
        }

        if (e.PropertyName is nameof(TerminalTabViewModel.FontSize) or nameof(TerminalTabViewModel.FontFamily) or nameof(TerminalTabViewModel.Theme) or nameof(TerminalTabViewModel.ScrollbackLines))
        {
            _ = Dispatcher.InvokeAsync(ApplyViewModelStateAsync);
        }
    }

    private async Task EnsureInitializedAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;

        try
        {
            await Browser.EnsureCoreWebView2Async();
            Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            Browser.CoreWebView2.Settings.IsWebMessageEnabled = true;
            Browser.WebMessageReceived += Browser_OnWebMessageReceived;
            Browser.NavigationCompleted += Browser_OnNavigationCompleted;

            var htmlPath = Path.Combine(AppContext.BaseDirectory, "Assets", "terminal-host.html");
            var html = await File.ReadAllTextAsync(htmlPath);
            Browser.NavigateToString(html);
        }
        catch (Exception ex)
        {
            ShowFallback(ex.Message);
        }
    }

    private async void Browser_OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        _documentReady = e.IsSuccess;
        if (!e.IsSuccess)
        {
            ShowFallback("终端 HTML 页面加载失败。");
            return;
        }

        _renderedBufferLength = 0;
        _isBufferSynchronized = false;
        await ApplyViewModelStateAsync();
    }

    private async void Browser_OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        var payload = TryGetWebMessagePayload(e);
        if (!TerminalWebMessage.TryParse(payload, out var message) || message is null)
        {
            return;
        }

        switch (message.Type)
        {
            case TerminalWebMessageType.Copy:
                if (!string.IsNullOrEmpty(message.Text))
                {
                    Clipboard.SetText(message.Text);
                }

                break;
            case TerminalWebMessageType.PasteRequest:
                if (Clipboard.ContainsText())
                {
                    await _viewModel.HandleWebMessageAsync(new TerminalWebMessage
                    {
                        Type = TerminalWebMessageType.Input,
                        Text = Clipboard.GetText(),
                    });
                }

                break;
            default:
                await _viewModel.HandleWebMessageAsync(message);
                break;
        }
    }

    private void OnOutputAppended(string text)
    {
        _ = Dispatcher.InvokeAsync(async () =>
        {
            if (!_documentReady || !_isBufferSynchronized || string.IsNullOrEmpty(text))
            {
                return;
            }

            await AppendOutputAsync(text);
            _renderedBufferLength += text.Length;
        });
    }

    private async Task ApplyViewModelStateAsync()
    {
        if (_viewModel is null || !_documentReady)
        {
            return;
        }

        var payload = new
        {
            fontSize = _viewModel.FontSize,
            fontFamily = _viewModel.FontFamily,
            scrollback = _viewModel.ScrollbackLines,
            theme = BuildTheme(_viewModel.Theme),
        };

        await Browser.ExecuteScriptAsync($"window.sshStudio && window.sshStudio.applyOptions({JsonSerializer.Serialize(payload)});");
        var buffer = _viewModel.GetBufferedOutput();
        if (_renderedBufferLength > buffer.Length)
        {
            _renderedBufferLength = 0;
        }

        if (!_isBufferSynchronized)
        {
            if (_renderedBufferLength == 0)
            {
                await ReplaceOutputAsync(buffer);
            }
            else if (_renderedBufferLength < buffer.Length)
            {
                await AppendOutputAsync(buffer[_renderedBufferLength..]);
            }

            _renderedBufferLength = buffer.Length;
            _isBufferSynchronized = true;
        }

        await Browser.ExecuteScriptAsync("window.sshStudio && window.sshStudio.focusTerminal();");
    }

    private async Task AppendOutputAsync(string text)
    {
        if (!_documentReady || string.IsNullOrEmpty(text))
        {
            return;
        }

        await Browser.ExecuteScriptAsync($"window.sshStudio && window.sshStudio.appendOutput({JsonSerializer.Serialize(text)});");
    }

    private async Task ReplaceOutputAsync(string text)
    {
        if (!_documentReady)
        {
            return;
        }

        await Browser.ExecuteScriptAsync($"window.sshStudio && window.sshStudio.replaceOutput({JsonSerializer.Serialize(text)});");
    }

    private void ShowFallback(string message)
    {
        FallbackMessage.Text = message;
        FallbackOverlay.Visibility = Visibility.Visible;
    }

    private static string TryGetWebMessagePayload(CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            return e.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            return e.WebMessageAsJson;
        }
    }

    private static object BuildTheme(TerminalTheme theme)
    {
        return theme switch
        {
            TerminalTheme.Light => new
            {
                background = "#F8FAFC",
                foreground = "#0F172A",
                cursor = "#2563EB",
                selectionBackground = "#BFDBFE",
            },
            TerminalTheme.Solarized => new
            {
                background = "#002B36",
                foreground = "#93A1A1",
                cursor = "#B58900",
                selectionBackground = "#073642",
            },
            _ => new
            {
                background = "#020617",
                foreground = "#E2E8F0",
                cursor = "#38BDF8",
                selectionBackground = "#1E293B",
            },
        };
    }
}
