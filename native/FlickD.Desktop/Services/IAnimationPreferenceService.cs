namespace FlickD.Desktop.Services;

public interface IAnimationPreferenceService
{
    bool AreAnimationsEnabled { get; }

    event EventHandler? PreferenceChanged;
}
