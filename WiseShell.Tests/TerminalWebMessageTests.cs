using System.Text.Json;
using WiseShell.Core.Models;

namespace WiseShell.Tests;

public sealed class TerminalWebMessageTests
{
    [Fact]
    public void TryParse_ReturnsResizeMessage()
    {
        var ok = TerminalWebMessage.TryParse("""{"kind":"resize","columns":120,"rows":40}""", out var message);

        Assert.True(ok);
        Assert.NotNull(message);
        Assert.Equal(TerminalWebMessageType.Resize, message!.Type);
        Assert.Equal(120, message.Columns);
        Assert.Equal(40, message.Rows);
    }

    [Fact]
    public void TryParse_ReturnsInputMessage()
    {
        var ok = TerminalWebMessage.TryParse("""{"kind":"input","text":"ls\r"}""", out var message);

        Assert.True(ok);
        Assert.Equal(TerminalWebMessageType.Input, message!.Type);
        Assert.Equal("ls\r", message.Text);
    }

    [Fact]
    public void TryParse_ReturnsInputMessageFromStringWrappedPayload()
    {
        var wrappedPayload = JsonSerializer.Serialize("""{"kind":"input","text":"pwd\r"}""");
        var ok = TerminalWebMessage.TryParse(wrappedPayload, out var message);

        Assert.True(ok);
        Assert.Equal(TerminalWebMessageType.Input, message!.Type);
        Assert.Equal("pwd\r", message.Text);
    }

    [Fact]
    public void TryParse_RejectsMalformedPayload()
    {
        var ok = TerminalWebMessage.TryParse("""{"kind":"resize","columns":0}""", out var message);

        Assert.False(ok);
        Assert.Null(message);
    }
}
