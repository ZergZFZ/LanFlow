using System;
using FlickD.Desktop.Models;

namespace FlickD.Desktop.Presentation;

public static class SettingsPreviewTransaction
{
    public static Settings Complete(
        SettingsPreviewSession session,
        bool accepted,
        Func<Settings, Settings> applyAndPersist)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(applyAndPersist);

        if (!accepted)
        {
            return session.Cancel();
        }

        var applied = applyAndPersist(session.Working.Clone());
        return session.Commit(applied);
    }
}
