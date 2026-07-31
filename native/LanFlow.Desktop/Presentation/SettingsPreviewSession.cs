using System;
using LanFlow.Desktop.Models;
using LanFlow.Desktop.Services;

namespace LanFlow.Desktop.Presentation;

public sealed class SettingsPreviewSession
{
    private Settings _original;

    public SettingsPreviewSession(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _original = settings.Clone();
        Working = settings.Clone();
    }

    public Settings Original => _original.Clone();
    public Settings Working { get; private set; }
    public bool HasChanges => !SettingsComparer.Equals(_original, Working);
    public event EventHandler<Settings>? PreviewRequested;

    public void Update(Action<Settings> mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        mutation(Working);
        SettingsNormalizer.ClampPreviewValues(Working);
        PreviewRequested?.Invoke(this, Working.Clone());
    }

    public Settings Commit() => Commit(Working);

    public Settings Commit(Settings applied)
    {
        ArgumentNullException.ThrowIfNull(applied);
        _original = applied.Clone();
        Working = applied.Clone();
        return _original.Clone();
    }

    public Settings Cancel()
    {
        Working = _original.Clone();
        PreviewRequested?.Invoke(this, Working.Clone());
        return Working.Clone();
    }
}
