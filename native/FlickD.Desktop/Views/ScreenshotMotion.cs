using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FlickD.Desktop.Views;

/// <summary>
/// 截图窗口的微动画工具集：统一时长与缓动曲线，避免各处硬编码。
/// 截图是高频操作，时长一律控制在 120-200ms 区间——低于 100ms 感知不到，
/// 高于 220ms 会拖慢连续截图节奏。
/// </summary>
internal static class ScreenshotMotion
{
    /// <summary>元素出现/消失。</summary>
    public const int FadeMs = 140;

    /// <summary>位移（工具条滑动、面板入场）。</summary>
    public const int SlideMs = 170;

    /// <summary>强调型过渡（编辑区聚焦）。</summary>
    public const int EmphasisMs = 200;

    /// <summary>标准缓出：起步快、收尾稳，用于入场。</summary>
    public static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    /// <summary>标准缓入：起步慢、收尾快，用于退场。</summary>
    public static readonly IEasingFunction EaseIn = new CubicEase { EasingMode = EasingMode.EaseIn };

    /// <summary>弹性曲线：用于需要“落定感”的位移（选区框、工具条落位）。</summary>
    public static readonly IEasingFunction EaseOutBack = new BackEase
    {
        EasingMode = EasingMode.EaseOut,
        Amplitude = 0.28,
    };

    /// <summary>
    /// 淡入并可选上浮。元素必须已处于布局中（Visibility=Visible）才会看到效果。
    /// fromOffset 为起始 Y 偏移（正数=从下方滑入）。
    /// </summary>
    public static void FadeIn(UIElement element, double fromOffset = 0, int durationMs = FadeMs,
        IEasingFunction? easing = null)
    {
        if (element is not FrameworkElement fe) return;
        easing ??= EaseOut;

        element.Opacity = 0;

        if (fromOffset != 0 && fe.RenderTransform is not TranslateTransform)
        {
            fe.RenderTransform = new TranslateTransform(0, fromOffset);
        }

        var fade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(durationMs)))
        {
            EasingFunction = easing,
        };
        element.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);

        if (fromOffset != 0 && fe.RenderTransform is TranslateTransform tt)
        {
            var slide = new DoubleAnimation(fromOffset, 0, new Duration(TimeSpan.FromMilliseconds(durationMs)))
            {
                EasingFunction = easing,
            };
            tt.BeginAnimation(TranslateTransform.YProperty, slide, HandoffBehavior.SnapshotAndReplace);
        }
    }

    /// <summary>淡出；完成后可选回调（用于隐藏元素）。</summary>
    public static void FadeOut(UIElement element, Action? completed = null, int durationMs = FadeMs,
        double toOffset = 0)
    {
        if (element is not FrameworkElement fe) return;

        var fade = new DoubleAnimation(fe.Opacity, 0, new Duration(TimeSpan.FromMilliseconds(durationMs)))
        {
            EasingFunction = EaseIn,
        };

        if (toOffset != 0 && fe.RenderTransform is TranslateTransform tt)
        {
            var slide = new DoubleAnimation(0, toOffset, new Duration(TimeSpan.FromMilliseconds(durationMs)))
            {
                EasingFunction = EaseIn,
            };
            tt.BeginAnimation(TranslateTransform.YProperty, slide, HandoffBehavior.SnapshotAndReplace);
        }

        fade.Completed += (_, _) => completed?.Invoke();
        element.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>
    /// 两段式出现：先把 Visibility 打开并立刻置 0 透明度，再动画到 1。
    /// WPF 中 Collapsed 元素没有渲染上下文，必须先可见再动画。
    /// </summary>
    public static void ShowAnimated(UIElement element, double fromOffset = 0, int durationMs = FadeMs)
    {
        element.Visibility = Visibility.Visible;
        element.Opacity = 0;
        // 等一帧让布局生效，否则首次渲染会直接跳到终态。
        element.Dispatcher.BeginInvoke(new Action(() =>
        {
            FadeIn(element, fromOffset, durationMs);
        }), System.Windows.Threading.DispatcherPriority.Render);
    }

    /// <summary>隐藏并淡出；Visibility 在动画结束后才置 Collapsed。</summary>
    public static void HideAnimated(UIElement element, int durationMs = FadeMs)
    {
        if (element.Visibility != Visibility.Visible) return;
        FadeOut(element, () =>
        {
            element.Visibility = Visibility.Collapsed;
            element.ClearValue(UIElement.OpacityProperty);
        }, durationMs);
    }

    /// <summary>清除元素上所有进行中的动画，恢复到无动画状态。</summary>
    public static void Reset(UIElement element)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1;
        if (element is FrameworkElement { RenderTransform: TranslateTransform tt })
        {
            tt.BeginAnimation(TranslateTransform.YProperty, null);
            tt.Y = 0;
        }
    }
}
