namespace LanFlow.Desktop.Views;

/// <summary>取消手势（Esc / 右键）要执行的动作。</summary>
public enum CancelAction
{
    /// <summary>文字输入中：取消本次文字输入。</summary>
    CancelText,
    /// <summary>标注态：返回框选态。</summary>
    ExitAnnotate,
    /// <summary>选择态：关闭截图窗口（不复制）。</summary>
    CloseWindow,
}

/// <summary>
/// 取消手势的决策规则。抽成纯函数便于测试：Esc 与右键必须共用同一套语义，
/// 避免两条输入路径行为漂移。
/// </summary>
public static class ScreenshotCancelPolicy
{
    public static CancelAction Decide(bool hasTextEditor, bool isAnnotating) =>
        hasTextEditor
            ? CancelAction.CancelText
            : isAnnotating
                ? CancelAction.ExitAnnotate
                : CancelAction.CloseWindow;
}
