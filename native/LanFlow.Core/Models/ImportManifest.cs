using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LanFlow.Desktop.Models;

public sealed class ImportManifest
{
    public string? Schema { get; init; }
    public string SchemaVersion { get; init; } = string.Empty;
    public IReadOnlyList<ImportManifestGroup> Groups { get; init; } = [];
}

public sealed class ImportManifestGroup
{
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<ImportManifestItem> Items { get; init; } = [];
}

public sealed class ImportManifestItem
{
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
}

public enum ImportGroupStatus
{
    NewGroup,
    MergeIntoExisting,
}

public enum ImportItemStatus
{
    NewItem,
    Existing,
    ManifestDuplicate,
    InvalidPath,
}

public sealed class ImportItemPreview : INotifyPropertyChanged
{
    private bool _isSelected;

    public required string Name { get; init; }
    public required string OriginalPath { get; init; }
    public required string ResolvedPath { get; init; }
    public required ImportItemStatus Status { get; init; }
    public string? ErrorMessage { get; init; }
    public string DisplayPath => string.IsNullOrWhiteSpace(ResolvedPath) ? OriginalPath : ResolvedPath;
    public bool CanSelect => Status == ImportItemStatus.NewItem;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            var normalized = CanSelect && value;
            if (_isSelected == normalized) return;
            _isSelected = normalized;
            OnPropertyChanged();
        }
    }

    public string StatusText => Status switch
    {
        ImportItemStatus.NewItem => "新项目",
        ImportItemStatus.Existing => "已存在",
        ImportItemStatus.ManifestDuplicate => "清单内重复",
        ImportItemStatus.InvalidPath => "无效路径",
        _ => Status.ToString(),
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ImportGroupPreview : INotifyPropertyChanged
{
    private bool _isUpdatingSelection;

    public required string Name { get; init; }
    public required ImportGroupStatus Status { get; init; }
    public string? ExistingGroupId { get; init; }
    public int ManifestOccurrenceCount { get; internal set; } = 1;
    public List<ImportItemPreview> Items { get; } = [];
    public bool CanSelect => Items.Any(item => item.CanSelect);

    public bool? IsSelected
    {
        get
        {
            var selectable = Items.Where(item => item.CanSelect).ToList();
            if (selectable.Count == 0) return false;
            var selected = selectable.Count(item => item.IsSelected);
            return selected == 0 ? false : selected == selectable.Count ? true : null;
        }
        set
        {
            if (_isUpdatingSelection || value is null) return;
            _isUpdatingSelection = true;
            try
            {
                foreach (var item in Items.Where(item => item.CanSelect))
                {
                    item.IsSelected = value.Value;
                }
            }
            finally
            {
                _isUpdatingSelection = false;
            }
            NotifySelectionChanged();
        }
    }

    public string StatusText => Status == ImportGroupStatus.NewGroup ? "新建分组" : "合并到已有分组";
    public string NoticeText => ManifestOccurrenceCount > 1 ? $"清单中的 {ManifestOccurrenceCount} 个同名分组已合并预览" : string.Empty;
    public int SelectedItemCount => Items.Count(item => item.IsSelected);

    internal void AttachItem(ImportItemPreview item)
    {
        Items.Add(item);
        item.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ImportItemPreview.IsSelected))
            {
                NotifySelectionChanged();
            }
        };
        OnPropertyChanged(nameof(CanSelect));
        NotifySelectionChanged();
    }

    internal void NotifyOccurrenceChanged()
    {
        OnPropertyChanged(nameof(ManifestOccurrenceCount));
        OnPropertyChanged(nameof(NoticeText));
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(SelectedItemCount));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    internal event EventHandler? SelectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ImportPreview : INotifyPropertyChanged
{
    public required string SourceFilePath { get; init; }
    public string SourceFileName => System.IO.Path.GetFileName(SourceFilePath);
    public required string SchemaVersion { get; init; }
    public List<ImportGroupPreview> Groups { get; } = [];

    public int TotalGroupCount => Groups.Count;
    public int TotalItemCount => Groups.Sum(group => group.Items.Count);
    public int SelectedItemCount => Groups.Sum(group => group.Items.Count(item => item.IsSelected));
    public int DuplicateItemCount => Groups.Sum(group => group.Items.Count(item => item.Status is ImportItemStatus.Existing or ImportItemStatus.ManifestDuplicate));
    public int InvalidItemCount => Groups.Sum(group => group.Items.Count(item => item.Status == ImportItemStatus.InvalidPath));
    public int SelectedNewGroupCount => Groups.Count(group => group.Status == ImportGroupStatus.NewGroup && group.Items.Any(item => item.IsSelected));
    public bool CanConfirm => SelectedItemCount > 0;
    public string SummaryText => $"将新建 {SelectedNewGroupCount} 个分组，导入 {SelectedItemCount} 个项目；跳过 {DuplicateItemCount} 个重复项，发现 {InvalidItemCount} 个无效项";

    internal void AttachGroup(ImportGroupPreview group)
    {
        Groups.Add(group);
        group.SelectionChanged += (_, _) => NotifySummaryChanged();
        NotifySummaryChanged();
    }

    private void NotifySummaryChanged()
    {
        OnPropertyChanged(nameof(TotalGroupCount));
        OnPropertyChanged(nameof(TotalItemCount));
        OnPropertyChanged(nameof(SelectedItemCount));
        OnPropertyChanged(nameof(DuplicateItemCount));
        OnPropertyChanged(nameof(InvalidItemCount));
        OnPropertyChanged(nameof(SelectedNewGroupCount));
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(SummaryText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ImportMergeResult
{
    public required AppConfig Config { get; init; }
    public int ImportedGroupCount { get; init; }
    public int ImportedItemCount { get; init; }
    public int SkippedItemCount { get; init; }
}