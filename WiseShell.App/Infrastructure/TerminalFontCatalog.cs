using System.Windows.Media;
using WiseShell.Core.Models;

namespace WiseShell.App.Infrastructure;

public static class TerminalFontCatalog
{
    private static readonly string[] PreferredFonts =
    [
        "Cascadia Mono",
        "Cascadia Code",
        "Consolas",
        "JetBrains Mono",
        "Fira Code",
        "Source Code Pro",
        "Lucida Console",
        "Courier New",
    ];

    public static IReadOnlyList<string> GetRecommendedFontFamilies()
    {
        var installedFonts = new HashSet<string>(
            Fonts.SystemFontFamilies.Select(font => font.Source),
            StringComparer.OrdinalIgnoreCase);

        var recommended = PreferredFonts
            .Where(installedFonts.Contains)
            .ToList();

        if (!recommended.Contains(TerminalDefaultsSettings.DefaultFontFamily, StringComparer.OrdinalIgnoreCase))
        {
            recommended.Add(TerminalDefaultsSettings.DefaultFontFamily);
        }

        return recommended;
    }

    public static string NormalizeFontFamily(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? TerminalDefaultsSettings.DefaultFontFamily
            : value.Trim();
    }
}
