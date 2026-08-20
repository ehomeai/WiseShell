using WiseShell.App.Infrastructure;

namespace WiseShell.App.ViewModels;

public sealed class TerminalDefaultsViewModel : ObservableObject
{
    private double _fontSize;
    private string _fontFamily;
    private bool _applyToMatchingSessions;

    public TerminalDefaultsViewModel(double fontSize, string fontFamily, bool applyToMatchingSessions)
    {
        _fontSize = fontSize;
        _fontFamily = TerminalFontCatalog.NormalizeFontFamily(fontFamily);
        _applyToMatchingSessions = applyToMatchingSessions;
    }

    public double FontSize
    {
        get => _fontSize;
        set => SetProperty(ref _fontSize, value);
    }

    public string FontFamily
    {
        get => _fontFamily;
        set => SetProperty(ref _fontFamily, value);
    }

    public bool ApplyToMatchingSessions
    {
        get => _applyToMatchingSessions;
        set => SetProperty(ref _applyToMatchingSessions, value);
    }
}
