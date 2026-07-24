using System.Globalization;
using System.Text;
using LeafLedger.Modules.Ledger.Application.Accounts;

namespace LeafLedger.Modules.Ledger.Infrastructure;

public static class AccountCsv
{
    public const string AccountsHeader = "kind,code,name,currency,group,isActive,validFrom,validTo,fxPolicy";
    public const string GroupsHeader = "name,rangeStart,rangeEnd,parent,fxPolicy";

    public static string WriteAccounts(IEnumerable<AccountImportRow> rows) =>
        Write(AccountsHeader, rows.Select(row => new[]
        {
            row.Kind,
            row.Code.ToString(CultureInfo.InvariantCulture),
            row.Name,
            row.Currency,
            row.Group ?? string.Empty,
            row.IsActive ? "TRUE" : "FALSE",
            row.ValidFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            row.ValidTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            row.FxPolicy ?? string.Empty,
        }));

    public static string WriteGroups(IEnumerable<GroupImportRow> rows) =>
        Write(GroupsHeader, rows.Select(row => new[]
        {
            row.Name,
            row.RangeStart.ToString(CultureInfo.InvariantCulture),
            row.RangeEnd.ToString(CultureInfo.InvariantCulture),
            row.Parent ?? string.Empty,
            row.FxPolicy ?? string.Empty,
        }));

    public static IReadOnlyList<AccountImportRow> ReadAccounts(string csv) => ReadAccountsWithWarnings(csv).Rows.Select(row => row.Value).ToArray();

    public static IReadOnlyList<GroupImportRow> ReadGroups(string csv) => ReadGroupsWithWarnings(csv).Rows.Select(row => row.Value).ToArray();

    public static CsvImportDocument<AccountImportRow> ReadAccountsWithWarnings(string csv)
    {
        var document = Read(csv, AccountsHeader.Split(','));
        var rows = new List<CsvImportRow<AccountImportRow>>();
        foreach (var record in document.Rows)
        {
            var values = record.Values;
            rows.Add(new CsvImportRow<AccountImportRow>(
                record.RowNumber,
                new AccountImportRow(
                    Required(values, "kind", record.RowNumber),
                    ParseInt(values, "code", record.RowNumber),
                    Required(values, "name", record.RowNumber),
                    Required(values, "currency", record.RowNumber),
                    Optional(values, "group"),
                    ParseBool(values, "isActive", record.RowNumber),
                    ParseDate(values, "validFrom", record.RowNumber),
                    ParseDate(values, "validTo", record.RowNumber),
                    Optional(values, "fxPolicy")),
                record.Warnings));
        }

        return new CsvImportDocument<AccountImportRow>(rows, document.Warnings);
    }

    public static CsvImportDocument<GroupImportRow> ReadGroupsWithWarnings(string csv)
    {
        var document = Read(csv, GroupsHeader.Split(','));
        var rows = new List<CsvImportRow<GroupImportRow>>();
        foreach (var record in document.Rows)
        {
            var values = record.Values;
            rows.Add(new CsvImportRow<GroupImportRow>(
                record.RowNumber,
                new GroupImportRow(
                    Required(values, "name", record.RowNumber),
                    ParseInt(values, "rangeStart", record.RowNumber),
                    ParseInt(values, "rangeEnd", record.RowNumber),
                    Optional(values, "parent"),
                    Optional(values, "fxPolicy")),
                record.Warnings));
        }

        return new CsvImportDocument<GroupImportRow>(rows, document.Warnings);
    }

    private static string Write(string header, IEnumerable<string[]> rows)
    {
        var builder = new StringBuilder(header).Append("\r\n");
        foreach (var row in rows)
        {
            builder.AppendJoin(',', row.Select(Escape)).Append("\r\n");
        }

        return builder.ToString();
    }

    private static CsvDocument Read(string csv, IReadOnlyList<string> requiredHeaders)
    {
        ArgumentNullException.ThrowIfNull(csv);
        var records = ParseRecords(csv.TrimStart('\uFEFF'));
        if (records.Count == 0 || records[0].Count == 0)
        {
            throw new FormatException("CSV must contain a header row.");
        }

        var headers = records[0].Select(header => header.Trim()).ToArray();
        var missing = requiredHeaders.Where(required => !headers.Contains(required, StringComparer.Ordinal)).ToArray();
        if (missing.Length > 0)
        {
            throw new FormatException($"CSV is missing required column(s): {string.Join(", ", missing)}.");
        }

        var warnings = headers
            .Where(header => !requiredHeaders.Contains(header, StringComparer.Ordinal))
            .Select(header => $"Ignored unsupported column '{header}'.")
            .ToArray();
        var rows = new List<CsvRecord>();
        for (var index = 1; index < records.Count; index++)
        {
            if (records[index].Count == 1 && string.IsNullOrEmpty(records[index][0]))
            {
                continue;
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var column = 0; column < headers.Length; column++)
            {
                values[headers[column]] = column < records[index].Count ? records[index][column] : string.Empty;
            }

            rows.Add(new CsvRecord(index + 1, values, warnings));
        }

        return new CsvDocument(rows, warnings);
    }

    private static List<List<string>> ParseRecords(string csv)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var value = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < csv.Length; index++)
        {
            var character = csv[index];
            if (quoted)
            {
                if (character == '"' && index + 1 < csv.Length && csv[index + 1] == '"')
                {
                    value.Append('"');
                    index++;
                }
                else if (character == '"')
                {
                    quoted = false;
                }
                else
                {
                    value.Append(character);
                }

                continue;
            }

            switch (character)
            {
                case '"' when value.Length == 0:
                    quoted = true;
                    break;
                case ',':
                    record.Add(value.ToString());
                    value.Clear();
                    break;
                case '\r':
                    if (index + 1 < csv.Length && csv[index + 1] == '\n') index++;
                    record.Add(value.ToString());
                    value.Clear();
                    records.Add(record);
                    record = new List<string>();
                    break;
                case '\n':
                    record.Add(value.ToString());
                    value.Clear();
                    records.Add(record);
                    record = new List<string>();
                    break;
                default:
                    value.Append(character);
                    break;
            }
        }

        if (quoted)
        {
            throw new FormatException("CSV contains an unterminated quoted field.");
        }

        if (value.Length > 0 || record.Count > 0)
        {
            record.Add(value.ToString());
            records.Add(record);
        }

        return records;
    }

    private static string Escape(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;

    private static string Required(IReadOnlyDictionary<string, string> values, string name, int rowNumber) =>
        values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new FormatException($"CSV row {rowNumber}: '{name}' is required.");

    private static string? Optional(IReadOnlyDictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value) ? value : null;

    private static int ParseInt(IReadOnlyDictionary<string, string> values, string name, int rowNumber) =>
        int.TryParse(Required(values, name, rowNumber), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : throw new FormatException($"CSV row {rowNumber}: '{name}' must be an integer.");

    private static bool ParseBool(IReadOnlyDictionary<string, string> values, string name, int rowNumber) =>
        bool.TryParse(Required(values, name, rowNumber), out var result)
            ? result
            : throw new FormatException($"CSV row {rowNumber}: '{name}' must be TRUE or FALSE.");

    private static DateOnly? ParseDate(IReadOnlyDictionary<string, string> values, string name, int rowNumber)
    {
        var value = Optional(values, name);
        return value is null
            ? null
            : DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result)
                ? result
                : throw new FormatException($"CSV row {rowNumber}: '{name}' must be yyyy-MM-dd.");
    }

    private sealed record CsvDocument(IReadOnlyList<CsvRecord> Rows, IReadOnlyList<string> Warnings);

    private sealed record CsvRecord(int RowNumber, IReadOnlyDictionary<string, string> Values, IReadOnlyList<string> Warnings);
}