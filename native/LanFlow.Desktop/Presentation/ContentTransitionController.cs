using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LanFlow.Desktop.Models;

namespace LanFlow.Desktop.Presentation;

public sealed class ContentTransitionController
{
    private static readonly Duration TransitionDuration = new(TimeSpan.FromMilliseconds(100));

    public static bool ShouldAnimate(
        string? animationMode,
        bool systemAnimationsEnabled,
        bool cacheHit)
    {
        if (!cacheHit)
        {
            return false;
        }

        return animationMode switch
        {
            SettingsOptionValues.AnimationOn => true,
            SettingsOptionValues.AnimationOff => false,
            _ => systemAnimationsEnabled,
        };
    }

    public async Task PlayAsync(
        FrameworkElement content,
        bool animate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var translation = EnsureTranslation(content);
        Stop(content, translation);

        if (!animate || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var opacityAnimation = new DoubleAnimation(0.92, 1.0, TransitionDuration)
        {
            FillBehavior = FillBehavior.Stop,
        };
        var translationAnimation = new DoubleAnimation(4.0, 0.0, TransitionDuration)
        {
            FillBehavior = FillBehavior.Stop,
        };

        content.BeginAnimation(UIElement.OpacityProperty, opacityAnimation, HandoffBehavior.SnapshotAndReplace);
        translation.BeginAnimation(TranslateTransform.YProperty, translationAnimation, HandoffBehavior.SnapshotAndReplace);

        try
        {
            await Task.Delay(TransitionDuration.TimeSpan, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // A newer group switch owns the content now. Stop without surfacing cancellation.
        }
        finally
        {
            Stop(content, translation);
        }
    }

    private static TranslateTransform EnsureTranslation(FrameworkElement content)
    {
        if (content.RenderTransform is TranslateTransform translation)
        {
            return translation;
        }

        translation = new TranslateTransform();
        content.RenderTransform = translation;
        return translation;
    }

    private static void Stop(FrameworkElement content, TranslateTransform translation)
    {
        content.BeginAnimation(UIElement.OpacityProperty, null);
        translation.BeginAnimation(TranslateTransform.YProperty, null);
        content.Opacity = 1.0;
        translation.Y = 0.0;
    }
}
