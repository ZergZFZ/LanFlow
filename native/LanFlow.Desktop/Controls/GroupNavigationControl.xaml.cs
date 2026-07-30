using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LanFlow.Desktop.Models;

namespace LanFlow.Desktop.Controls;

public sealed class GroupNavigationEventArgs : RoutedEventArgs
{
    public GroupNavigationEventArgs(RoutedEvent routedEvent, object source, Group group)
        : base(routedEvent, source)
    {
        Group = group;
    }

    public Group Group { get; }
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

    private void GroupItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        RaiseGroupEvent(sender, GroupInvokedEvent);

    private void GroupItem_MouseEnter(object sender, MouseEventArgs e) =>
        RaiseGroupEvent(sender, GroupHoveredEvent);

    private void GroupItem_DragEnter(object sender, DragEventArgs e) =>
        RaiseGroupEvent(sender, GroupDragHoveredEvent);

    private void GroupItem_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(LauncherItem))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void GroupItem_Drop(object sender, DragEventArgs e)
    {
        RaiseGroupEvent(sender, GroupDroppedEvent);
        e.Handled = true;
    }

    private void RaiseGroupEvent(object sender, RoutedEvent routedEvent)
    {
        if (sender is ListBoxItem { DataContext: Group group })
        {
            RaiseEvent(new GroupNavigationEventArgs(routedEvent, this, group));
        }
    }
}
