namespace InstantAIGate.Dashboard.Services;

/// <summary>
/// Stable identifiers for every user-initiated dashboard command.
/// Ids are the key of the per-command state machine in <see cref="UiCommandService"/>,
/// so buttons anywhere in the tree can query running/failed state without prop drilling.
/// </summary>
public static class UiCommandIds
{
    public const string Connect = "session.connect";
    public const string Disconnect = "session.disconnect";
    public const string ForceGc = "admin.force-gc";
    public const string PurgeAll = "admin.purge-all";
    public const string ForceGcAndPurgeAll = "admin.gc-purge-all";
    public const string SetQueueLimit = "admin.queue-limit";

    public static string PurgeModel(string repoId) => $"admin.purge:{repoId}";

    public static string UnloadModel(string repoId) => $"admin.unload:{repoId}";

    public static string KillSession(string sessionId) => $"admin.kill:{sessionId}";
}