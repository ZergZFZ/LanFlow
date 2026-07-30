namespace LanFlow.Desktop.Services;

public interface IAnimationPreferenceService
{
    bool AreAnimationsEnabled { get; }

    event EventHandler? PreferenceChanged;
}
