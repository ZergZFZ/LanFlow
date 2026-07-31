using System;
using LanFlow.Desktop.Models;

namespace LanFlow.Desktop.Presentation;

public enum UnsavedCloseDecision
{
    ApplyAndClose,
    Discard,
    KeepEditing,
}

public static class SettingsCloseFlow
{
    public static bool TryComplete(
        SettingsPreviewSession session,
        UnsavedCloseDecision decision,
        Action flush,
        Func<Settings, Settings> applyAndPersist)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(flush);
        ArgumentNullException.ThrowIfNull(applyAndPersist);

        flush();
        if (!session.HasChanges)
        {
            return true;
        }

        switch (decision)
        {
            case UnsavedCloseDecision.KeepEditing:
                return false;
            case UnsavedCloseDecision.Discard:
                session.Cancel();
                return true;
            case UnsavedCloseDecision.ApplyAndClose:
                SettingsPreviewTransaction.Complete(session, accepted: true, applyAndPersist);
                return true;
            default:
                throw new ArgumentOutOfRangeException(nameof(decision), decision, null);
        }
    }
}
