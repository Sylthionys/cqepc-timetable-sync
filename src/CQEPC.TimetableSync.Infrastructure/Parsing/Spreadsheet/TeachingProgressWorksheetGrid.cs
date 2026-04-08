using System.Text;

namespace CQEPC.TimetableSync.Infrastructure.Parsing.Spreadsheet;

internal sealed class TeachingProgressWorksheetGrid
{
    private readonly string?[,] cells;

    public TeachingProgressWorksheetGrid(string name, string?[,] sourceCells)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Worksheet name cannot be empty.", nameof(name));
        }

        ArgumentNullException.ThrowIfNull(sourceCells);

        var sourceRowCount = sourceCells.GetLength(0);
        var sourceColumnCount = sourceCells.GetLength(1);
        var lastUsedRow = -1;
        var lastUsedColumn = -1;

        for (var rowIndex = 0; rowIndex < sourceRowCount; rowIndex++)
        {
            for (var columnIndex = 0; columnIndex < sourceColumnCount; columnIndex++)
            {
                if (string.IsNullOrWhiteSpace(sourceCells[rowIndex, columnIndex]))
                {
                    continue;
                }

                lastUsedRow = Math.Max(lastUsedRow, rowIndex);
                lastUsedColumn = Math.Max(lastUsedColumn, columnIndex);
            }
        }

        Name = name.Trim();

        if (lastUsedRow < 0 || lastUsedColumn < 0)
        {
            cells = new string?[0, 0];
            RowCount = 0;
            ColumnCount = 0;
            return;
        }

        RowCount = lastUsedRow + 1;
        ColumnCount = lastUsedColumn + 1;
        cells = new string?[RowCount, ColumnCount];

        for (var rowIndex = 0; rowIndex < RowCount; rowIndex++)
        {
            for (var columnIndex = 0; columnIndex < ColumnCount; columnIndex++)
            {
                cells[rowIndex, columnIndex] = Normalize(sourceCells[rowIndex, columnIndex]);
            }
        }
    }

    public string Name { get; }

    public int RowCount { get; }

    public int ColumnCount { get; }

    public string? GetText(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || rowIndex >= RowCount || columnIndex < 0 || columnIndex >= ColumnCount)
        {
            return null;
        }

        return cells[rowIndex, columnIndex];
    }

    public IEnumerable<TeachingProgressGridCell> EnumerateNonEmptyCells()
    {
        for (var rowIndex = 0; rowIndex < RowCount; rowIndex++)
        {
            for (var columnIndex = 0; columnIndex < ColumnCount; columnIndex++)
            {
                var text = cells[rowIndex, columnIndex];
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                yield return new TeachingProgressGridCell(rowIndex, columnIndex, text);
            }
        }
    }

    public string GetSourceAnchor(int rowIndex, int columnIndex) =>
        $"{Name}!{ToCellAddress(rowIndex, columnIndex)}";

    public static string ToCellAddress(int rowIndex, int columnIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rowIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(columnIndex);

        var current = columnIndex + 1;
        var builder = new StringBuilder();
        while (current > 0)
        {
            current--;
            builder.Insert(0, (char)('A' + (current % 26)));
            current /= 26;
        }

        builder.Append(rowIndex + 1);
        return builder.ToString();
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}

internal readonly record struct TeachingProgressGridCell(int RowIndex, int ColumnIndex, string Text);
