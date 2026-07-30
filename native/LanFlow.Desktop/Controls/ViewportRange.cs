namespace LanFlow.Desktop.Controls;

public readonly record struct ViewportRange(
    int FirstIndex,
    int LastIndex,
    int Columns)
{
    public static ViewportRange Empty => new(-1, -1, 1);

    public bool Contains(int index) =>
        FirstIndex >= 0 && index >= FirstIndex && index <= LastIndex;
}

public enum NavigationDirection
{
    Left,
    Right,
    Up,
    Down
}
