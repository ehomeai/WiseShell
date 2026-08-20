using System.Text.Json;
using WiseShell.Core.Infrastructure;

namespace WiseShell.Core.Models;

public enum TerminalWebMessageType
{
    Input = 0,
    Resize = 1,
    Copy = 2,
    PasteRequest = 3,
    Search = 4,
}

public sealed class TerminalWebMessage
{
    public TerminalWebMessageType Type { get; init; }

    public string? Text { get; init; }

    public int? Columns { get; init; }

    public int? Rows { get; init; }

    public static bool TryParse(string json, out TerminalWebMessage? message)
    {
        message = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            if (TryUnwrapJsonString(json, out var innerJson))
            {
                return TryParse(innerJson, out message);
            }

            var dto = JsonSerializer.Deserialize<TerminalWebMessageDto>(json, JsonOptionsProvider.Json);
            if (dto is null || string.IsNullOrWhiteSpace(dto.Kind))
            {
                return false;
            }

            message = dto.Kind.ToLowerInvariant() switch
            {
                "input" => new TerminalWebMessage
                {
                    Type = TerminalWebMessageType.Input,
                    Text = dto.Text ?? string.Empty,
                },
                "resize" when dto.Columns is > 0 && dto.Rows is > 0 => new TerminalWebMessage
                {
                    Type = TerminalWebMessageType.Resize,
                    Columns = dto.Columns,
                    Rows = dto.Rows,
                },
                "copy" => new TerminalWebMessage
                {
                    Type = TerminalWebMessageType.Copy,
                    Text = dto.Text ?? string.Empty,
                },
                "pasterequest" => new TerminalWebMessage
                {
                    Type = TerminalWebMessageType.PasteRequest,
                },
                "search" => new TerminalWebMessage
                {
                    Type = TerminalWebMessageType.Search,
                    Text = dto.Text ?? string.Empty,
                },
                _ => null,
            };

            return message is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryUnwrapJsonString(string json, out string innerJson)
    {
        innerJson = string.Empty;

        if (string.IsNullOrWhiteSpace(json) || json.TrimStart()[0] != '"')
        {
            return false;
        }

        try
        {
            var value = JsonSerializer.Deserialize<string>(json, JsonOptionsProvider.Json);
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            innerJson = value;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed class TerminalWebMessageDto
    {
        public string? Kind { get; set; }

        public string? Text { get; set; }

        public int? Columns { get; set; }

        public int? Rows { get; set; }
    }
}
