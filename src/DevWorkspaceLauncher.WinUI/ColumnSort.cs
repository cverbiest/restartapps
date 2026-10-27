using Microsoft.UI.Xaml.Controls;

namespace DevWorkspaceLauncher.WinUI;

/// <summary>
/// Click-to-sort column headers for a grid: a header click sorts by that column, a second click
/// reverses it, and an arrow marks the column in use. The headers are buttons (style SortHeader) whose
/// content is the caption TextBlock.
/// </summary>
internal sealed class ColumnSort<T>
{
    private readonly (Button Header, string Caption, Func<T, object?> Key)[] _columns;
    private int? _column;
    private bool _descending;

    /// <param name="refresh">Rebuilds the grid; it calls <see cref="Apply"/> on its rows.</param>
    public ColumnSort(Action refresh, params (Button Header, Func<T, object?> Key)[] columns)
    {
        _columns = columns.Select(column => (column.Header, ((TextBlock)column.Header.Content).Text, column.Key)).ToArray();
        for (var i = 0; i < _columns.Length; i++)
        {
            var index = i;
            _columns[i].Header.Click += (_, _) =>
            {
                _descending = _column == index && !_descending;
                _column = index;
                ShowArrow();
                refresh();
            };
        }
    }

    /// <summary>The rows in the chosen order; in their own order until a header is clicked.</summary>
    public IEnumerable<T> Apply(IEnumerable<T> rows)
    {
        if (_column is not { } index)
        {
            return rows;
        }

        var key = _columns[index].Key;
        return _descending ? rows.OrderByDescending(key, Comparer) : rows.OrderBy(key, Comparer);
    }

    /// <summary>Text ignoring case, the way people read it; anything else (dates, flags) as itself.</summary>
    private static readonly IComparer<object?> Comparer = Comparer<object?>.Create((a, b) => a is string x && b is string y
        ? StringComparer.CurrentCultureIgnoreCase.Compare(x, y)
        : Comparer<object?>.Default.Compare(a, b));

    private void ShowArrow()
    {
        for (var i = 0; i < _columns.Length; i++)
        {
            var (header, caption, _) = _columns[i];
            ((TextBlock)header.Content).Text = i == _column ? $"{caption} {(_descending ? "▼" : "▲")}" : caption;
        }
    }
}
