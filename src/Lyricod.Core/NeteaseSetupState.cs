namespace Lyricod.Core;

public enum NeteaseStage { Downloading, WaitingForPermission }

public enum NeteaseResult { Done, Cancelled, CouldntDownload, Failed }

/// <summary>One setup's state, kept while settings pages and windows come and go. Used on the UI thread.</summary>
public sealed class NeteaseSetupState
{
    int _operation;
    public bool Busy { get; private set; }
    public bool Removing { get; private set; }
    public NeteaseStage Stage { get; private set; }
    public (NeteaseResult Result, string? Error)? Outcome { get; private set; }
    public event Action? Changed;

    public int? Start(bool removing)
    {
        if (Busy) return null;
        Busy = true;
        Removing = removing;
        Stage = NeteaseStage.Downloading;
        Outcome = null;
        int operation = ++_operation;
        Notify();
        return operation;
    }

    public void Report(int operation, NeteaseStage stage)
    {
        if (!Busy || operation != _operation) return;
        Stage = stage;
        Notify();
    }

    public void Complete(int operation, (NeteaseResult Result, string? Error) outcome)
    {
        if (!Busy || operation != _operation) return;
        Outcome = outcome;
        Busy = false;
        Notify();
    }

    void Notify()
    {
        if (Changed is not { } changed) return;
        foreach (Action refresh in changed.GetInvocationList())
        {
            try { refresh(); }
            catch (Exception) { /* a settings page closed meanwhile */ }
        }
    }
}
