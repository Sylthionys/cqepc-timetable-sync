using System.Globalization;
using System.Text;
using ExcelDataReader;

namespace CQEPC.TimetableSync.Infrastructure.Parsing.Spreadsheet;

internal interface ITeachingProgressWorkbookReader
{
    IReadOnlyList<TeachingProgressWorksheetGrid> ReadVisibleWorksheets(string filePath, CancellationToken cancellationToken);
}

internal sealed class TeachingProgressWorkbookReader : ITeachingProgressWorkbookReader
{
    private static readonly object EncodingRegistrationLock = new();
    private static bool codePagesRegistered;

    public IReadOnlyList<TeachingProgressWorksheetGrid> ReadVisibleWorksheets(string filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Workbook path cannot be empty.", nameof(filePath));
        }

        RegisterCodePages();

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = ExcelReaderFactory.CreateReader(stream, new ExcelReaderConfiguration
        {
            LeaveOpen = false,
        });

        var worksheets = new List<TeachingProgressWorksheetGrid>();

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            var isVisible = string.IsNullOrWhiteSpace(reader.VisibleState)
                || string.Equals(reader.VisibleState, "visible", StringComparison.OrdinalIgnoreCase);
            var worksheetName = string.IsNullOrWhiteSpace(reader.Name) ? "Sheet" : reader.Name;
            var rows = new List<string?[]>();

            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var values = new string?[reader.FieldCount];
                for (var columnIndex = 0; columnIndex < reader.FieldCount; columnIndex++)
                {
                    values[columnIndex] = NormalizeValue(reader.GetValue(columnIndex));
                }

                rows.Add(values);
            }

            if (!isVisible)
            {
                continue;
            }

            var rowCount = rows.Count;
            var columnCount = rows.Count == 0 ? 0 : rows.Max(static row => row.Length);
            var cells = new string?[rowCount, columnCount];
            for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
            {
                var currentRow = rows[rowIndex];
                for (var columnIndex = 0; columnIndex < currentRow.Length; columnIndex++)
                {
                    cells[rowIndex, columnIndex] = currentRow[columnIndex];
                }
            }

            worksheets.Add(new TeachingProgressWorksheetGrid(worksheetName, cells));
        }
        while (reader.NextResult());

        return worksheets;
    }

    private static void RegisterCodePages()
    {
        if (codePagesRegistered)
        {
            return;
        }

        lock (EncodingRegistrationLock)
        {
            if (codePagesRegistered)
            {
                return;
            }

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            codePagesRegistered = true;
        }
    }

    private static string? NormalizeValue(object? value) =>
        value switch
        {
            null => null,
            DBNull => null,
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            double number when Math.Abs(number - Math.Round(number)) < 0.0000001d =>
                Math.Round(number).ToString(CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
}
