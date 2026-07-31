using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace LanFlow.Core.Collections;

public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceRange(IReadOnlyList<T> values)
    {
        Items.Clear();
        for (var index = 0; index < values.Count; index++) Items.Add(values[index]);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}