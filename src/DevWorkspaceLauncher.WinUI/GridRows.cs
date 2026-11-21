using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;

namespace DevWorkspaceLauncher.WinUI;

/// <summary>
/// A grid row that can take a fresh copy of itself in place, so a refresh changes its cells without
/// rebuilding the row. Its template binds with Mode=OneWay.
/// </summary>
public abstract class GridRow<TSelf> : INotifyPropertyChanged where TSelf : GridRow<TSelf>
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>What identifies the row from one refresh to the next (compared ignoring case).</summary>
    public abstract string Key { get; }

    /// <summary>Takes over the fresh row's data.</summary>
    protected abstract void CopyFrom(TSelf fresh);

    internal void Update(TSelf fresh)
    {
        CopyFrom(fresh);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}

/// <summary>
/// Brings a grid's rows in line with a fresh list without redrawing the grid: rows that stay are updated
/// in place, the others are removed or inserted, and the order is fixed with moves. A full redraw makes
/// the grid flash and can scroll the current row out of view.
/// </summary>
internal static class GridRows
{
    private static readonly StringComparer KeyComparer = StringComparer.OrdinalIgnoreCase;

    public static void Sync<T>(ListView list, ObservableCollection<T> items, IReadOnlyList<T> fresh) where T : GridRow<T>
    {
        // A moved row loses its selection in the ListView; put it back afterwards.
        var selected = list.SelectedItems.OfType<T>().Select(row => row.Key).ToHashSet(KeyComparer);
        var wanted = fresh.Select(row => row.Key).ToHashSet(KeyComparer);

        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(items[i].Key))
            {
                items.RemoveAt(i);
            }
        }

        for (var i = 0; i < fresh.Count; i++)
        {
            var at = IndexOf(items, fresh[i].Key, i);
            if (at < 0)
            {
                items.Insert(i, fresh[i]);
                continue;
            }

            if (at != i)
            {
                items.Move(at, i);
            }

            items[i].Update(fresh[i]);
        }

        // Left over only when keys repeat.
        while (items.Count > fresh.Count)
        {
            items.RemoveAt(items.Count - 1);
        }

        foreach (var row in items.Where(row => selected.Contains(row.Key) && !list.SelectedItems.Contains(row)).ToList())
        {
            list.SelectedItems.Add(row);
        }
    }

    private static int IndexOf<T>(ObservableCollection<T> items, string key, int from) where T : GridRow<T>
    {
        for (var j = from; j < items.Count; j++)
        {
            if (KeyComparer.Equals(items[j].Key, key))
            {
                return j;
            }
        }

        return -1;
    }
}
