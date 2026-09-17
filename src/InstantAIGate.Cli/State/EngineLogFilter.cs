namespace InstantAIGate.Cli.State;

using System.IO;
using System.Text;

public class DebugState
{
    public bool IsEnabled { get; set; } = false; // Off by default
}

public class EngineLogFilter : TextWriter
{
    private readonly TextWriter _originalWriter;
    private readonly DebugState _debugState;

    public EngineLogFilter(TextWriter originalWriter, DebugState debugState)
    {
        _originalWriter = originalWriter;
        _debugState = debugState;
    }

    public override Encoding Encoding => _originalWriter.Encoding;

    public override void Write(char value)
    {
        if (_debugState.IsEnabled) _originalWriter.Write(value);
    }

    public override void Write(string? value)
    {
        if (_debugState.IsEnabled) _originalWriter.Write(value);
    }

    public override void Write(char[] buffer, int index, int count)
    {
        if (_debugState.IsEnabled) _originalWriter.Write(buffer, index, count);
    }

    public override void WriteLine(string? value)
    {
        if (_debugState.IsEnabled) _originalWriter.WriteLine(value);
    }
}