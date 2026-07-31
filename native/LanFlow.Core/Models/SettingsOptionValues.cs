namespace LanFlow.Desktop.Models;

public static class SettingsOptionValues
{
    public const string GridLayout = "grid";
    public const string CardLayout = "card";
    public const string GroupTop = "top";
    public const string GroupLeft = "left";
    public const string GroupSwitchClick = "click";
    public const string GroupSwitchHover = "hover";
    public const string TransparencyLayered = "layered";
    public const string TransparencyWholeWindow = "wholeWindow";
    public const string AnimationSystem = "system";
    public const string AnimationOn = "on";
    public const string AnimationOff = "off";

    public const int DefaultGroupHoverDelayMs = 100;
    public const int MinGroupHoverDelayMs = 0;
    public const int MaxGroupHoverDelayMs = 500;
}
