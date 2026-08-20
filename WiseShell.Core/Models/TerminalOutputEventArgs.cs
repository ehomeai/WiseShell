namespace WiseShell.Core.Models;

public sealed class TerminalOutputEventArgs : EventArgs
{
    public TerminalOutputEventArgs(string text)
    {
        Text = text;
    }

    public string Text { get; }
}
