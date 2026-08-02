using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LanFlow.Desktop.Models;

namespace LanFlow.Desktop.Controls;

public sealed class GroupNavigationEventArgs : RoutedEventArgs
{
    public GroupNavigationEventArgs(
        RoutedEvent routedEvent,
        object source,
        Group group,
        bool isActive = true)
        : base(routedEvent, source)
    {
        Group = group;
        IsActive = isActive;
    }

    public Group Group { get; }

    public bool IsActive { get; }

    // 分组排序拖拽的目标分组；非排序场景为 null。
    public Group? TargetGroup { get; init; }
}

public partial class GroupNavigationControl : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(GroupNavigationControl),
            new PropertyMetadata(null));

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(
            nameof(SelectedItem),
            typeof(Group),
            typeof(GroupNavigationControl),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty GroupLayoutProperty =
        DependencyProperty.Register(
            nameof(GroupLayout),
            typeof(string),
            typeof(GroupNavigationControl),
            new PropertyMetadata(SettingsOptionValues.GroupLeft));

    public static readonly DependencyProperty GroupLabelSizeProperty =
        DependencyProperty.Register(
            nameof(GroupLabelSize),
            typeof(double),
            typeof(GroupNavigationControl),
            new PropertyMetadata(36d));

    public static readonly DependencyProperty GroupLabelFontSizeProperty =
        DependencyProperty.Register(
            nameof(GroupLabelFontSize),
            typeof(double),
            typeof(GroupNavigationControl),
            new PropertyMetadata(13d));

    public static readonly DependencyProperty GroupNavigationWidthProperty =
        DependencyProperty.Register(
            nameof(GroupNavigationWidth),
            typeof(double),
            typeof(GroupNavigationControl),
            new PropertyMetadata(132d));

    public static readonly DependencyProperty IsEditModeProperty =
        DependencyProperty.Register(
            nameof(IsEditMode),
            typeof(bool),
            typeof(GroupNavigationControl),
            new PropertyMetadata(false));

    public static readonly RoutedEvent GroupInvokedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(GroupInvoked),
            RoutingStrategy.Bubble,
            typeof(EventHandler<GroupNavigationEventArgs>),
            typeof(GroupNavigationControl));

    public static readonly RoutedEvent GroupHoveredEvent =
        EventManager.RegisterRoutedEvent(
            nameof(GroupHovered),
            RoutingStrategy.Bubble,
            typeof(EventHandler<GroupNavigationEventArgs>),
            typeof(GroupNavigationControl));

    public static readonly RoutedEvent GroupDragHoveredEvent =
        EventManager.RegisterRoutedEvent(
            nameof(GroupDragHovered),
            RoutingStrategy.Bubble,
            typeof(EventHandler<GroupNavigationEventArgs>),
            typeof(GroupNavigationControl));

    public static readonly RoutedEvent GroupDroppedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(GroupDropped),
            RoutingStrategy.Bubble,
            typeof(EventHandler<GroupNavigationEventArgs>),
            typeof(GroupNavigationControl));

    // 编辑模式下分组标签拖拽排序请求：source 为目标插入位置前的分组，
    // 由 MainWindow 依据目标分组计算插入索引并调整 Groups 顺序。
    public static readonly RoutedEvent GroupReorderRequestedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(GroupReorderRequested),
            RoutingStrategy.Bubble,
            typeof(EventHandler<GroupNavigationEventArgs>),
            typeof(GroupNavigationControl));

    private Group? _dragSourceGroup;
    private ListBoxItem? _dragSourceItem;

    public GroupNavigationControl()
    {
        InitializeComponent();
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public Group? SelectedItem
    {
        get => (Group?)GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public string GroupLayout
    {
        get => (string)GetValue(GroupLayoutProperty);
        set => SetValue(GroupLayoutProperty, value);
    }

    public double GroupLabelSize
    {
        get => (double)GetValue(GroupLabelSizeProperty);
        set => SetValue(GroupLabelSizeProperty, value);
    }

    public double GroupLabelFontSize
    {
        get => (double)GetValue(GroupLabelFontSizeProperty);
        set => SetValue(GroupLabelFontSizeProperty, value);
    }

    public double GroupNavigationWidth
    {
        get => (double)GetValue(GroupNavigationWidthProperty);
        set => SetValue(GroupNavigationWidthProperty, value);
    }

    public bool IsEditMode
    {
        get => (bool)GetValue(IsEditModeProperty);
        set => SetValue(IsEditModeProperty, value);
    }

    public event EventHandler<GroupNavigationEventArgs> GroupInvoked
    {
        add => AddHandler(GroupInvokedEvent, value);
        remove => RemoveHandler(GroupInvokedEvent, value);
    }

    public event EventHandler<GroupNavigationEventArgs> GroupHovered
    {
        add => AddHandler(GroupHoveredEvent, value);
        remove => RemoveHandler(GroupHoveredEvent, value);
    }

    public event EventHandler<GroupNavigationEventArgs> GroupDragHovered
    {
        add => AddHandler(GroupDragHoveredEvent, value);
        remove => RemoveHandler(GroupDragHoveredEvent, value);
    }

    public event EventHandler<GroupNavigationEventArgs> GroupDropped
    {
        add => AddHandler(GroupDroppedEvent, value);
        remove => RemoveHandler(GroupDroppedEvent, value);
    }

    public event EventHandler<GroupNavigationEventArgs> GroupReorderRequested
    {
        add => AddHandler(GroupReorderRequestedEvent, value);
        remove => RemoveHandler(GroupReorderRequestedEvent, value);
    }

    private void GroupItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsEditMode && sender is ListBoxItem { DataContext: Group group } container)
        {
            // 编辑模式：按住分组标签进入拖拽排序；先记录源，避免点击即切换。
            _dragSourceGroup = group;
            _dragSourceItem = container;
            _dragStartPoint = e.GetPosition(GroupList);
            e.Handled = true;
            return;
        }

        RaiseGroupEvent(sender, GroupInvokedEvent);
        e.Handled = true;
    }

    private void GroupItem_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!IsEditMode ||
            _dragSourceGroup is null ||
            e.LeftButton != MouseButtonState.Pressed ||
            sender is not ListBoxItem)
        {
            return;
        }

        if (sender is not ListBoxItem container || !ReferenceEquals(container, _dragSourceItem))
        {
            return;
        }

        var position = e.GetPosition(GroupList);
        if (Math.Abs(position.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var source = _dragSourceGroup;
        _dragSourceGroup = null;
        _dragSourceItem = null;
        try
        {
            DragDrop.DoDragDrop(
                GroupList,
                new DataObject(typeof(Group), source),
                DragDropEffects.Move);
        }
        finally
        {
            ClearReorderIndicator();
        }
    }

    private Point _dragStartPoint;

    private void GroupItem_MouseEnter(object sender, MouseEventArgs e) =>
        RaiseGroupEvent(sender, GroupHoveredEvent, isActive: true);

    private void GroupItem_MouseLeave(object sender, MouseEventArgs e) =>
        RaiseGroupEvent(sender, GroupHoveredEvent, isActive: false);

    private void GroupItem_DragEnter(object sender, DragEventArgs e) =>
        RaiseGroupEvent(sender, GroupDragHoveredEvent, isActive: true);

    private void GroupItem_DragOver(object sender, DragEventArgs e)
    {
        if (IsEditMode && e.Data.GetDataPresent(typeof(Group)))
        {
            e.Effects = DragDropEffects.Move;
            UpdateReorderIndicator(sender, e);
            e.Handled = true;
            return;
        }

        e.Effects = e.Data.GetDataPresent(typeof(LauncherItem))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void GroupItem_DragLeave(object sender, DragEventArgs e)
    {
        if (IsEditMode && e.Data.GetDataPresent(typeof(Group)))
        {
            ClearReorderIndicator();
        }

        RaiseGroupEvent(sender, GroupDragHoveredEvent, isActive: false);
    }

    private void GroupItem_Drop(object sender, DragEventArgs e)
    {
        if (IsEditMode && e.Data.GetData(typeof(Group)) is Group source &&
            sender is ListBoxItem { DataContext: Group target })
        {
            RaiseEvent(new GroupNavigationEventArgs(
                GroupReorderRequestedEvent,
                this,
                source,
                isActive: true)
            {
                // 目标分组通过扩展信息传给 MainWindow；排序逻辑在 MainWindow。
                TargetGroup = target,
            });
            ClearReorderIndicator();
            e.Handled = true;
            return;
        }

        RaiseGroupEvent(sender, GroupDroppedEvent);
        e.Handled = true;
    }

    private void UpdateReorderIndicator(object sender, DragEventArgs e)
    {
        if (sender is ListBoxItem container)
        {
            container.Tag = "reorder-target";
        }
    }

    private void ClearReorderIndicator()
    {
        foreach (var item in GroupList.Items)
        {
            if (GroupList.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem container &&
                container.Tag as string == "reorder-target")
            {
                container.Tag = null;
            }
        }
    }

    private void RaiseGroupEvent(
        object sender,
        RoutedEvent routedEvent,
        bool isActive = true)
    {
        if (sender is ListBoxItem { DataContext: Group group })
        {
            RaiseEvent(new GroupNavigationEventArgs(routedEvent, this, group, isActive));
        }
    }
}
