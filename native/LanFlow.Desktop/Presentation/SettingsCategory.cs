namespace LanFlow.Desktop.Presentation;

public sealed record SettingsCategory(
    string Id,
    string Title,
    string Description,
    IReadOnlyList<string> SettingKeys);
