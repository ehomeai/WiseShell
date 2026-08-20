namespace WiseShell.Core.Models;

public sealed class TerminalDefaultsSettings
{
    public const double DefaultFontSize = 18d;
    public const string DefaultFontFamily = "Consolas";

    public double FontSize { get; set; } = DefaultFontSize;

    public string FontFamily { get; set; } = DefaultFontFamily;

    public TerminalDefaultsSettings CreateCopy()
    {
        return new TerminalDefaultsSettings
        {
            FontSize = FontSize,
            FontFamily = FontFamily,
        };
    }
}
