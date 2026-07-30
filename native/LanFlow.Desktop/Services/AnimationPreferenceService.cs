using System.ComponentModel;
using System.Windows;

namespace LanFlow.Desktop.Services;

public sealed class AnimationPreferenceService : IAnimationPreferenceService, IDisposable
{
    private bool _isDisposed;

    public AnimationPreferenceService()
    {
        SystemParameters.StaticPropertyChanged += SystemParameters_StaticPropertyChanged;
    }

    public bool AreAnimationsEnabled
    {
        get
        {
            try
            {
                return SystemParameters.ClientAreaAnimation;
            }
            catch
            {
                return true;
            }
        }
    }

    public event EventHandler? PreferenceChanged;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        SystemParameters.StaticPropertyChanged -= SystemParameters_StaticPropertyChanged;
    }

    private void SystemParameters_StaticPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) ||
            string.Equals(e.PropertyName, nameof(SystemParameters.ClientAreaAnimation), StringComparison.Ordinal))
        {
            PreferenceChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
