namespace InstantAIGate.Cli.State;

public class PipelineContext
{
    public string SelectedBranch { get; set; } = string.Empty;
    public string NewVersion { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public string CommitMessage { get; set; } = string.Empty;
}