using System.Globalization;
using System.Text.Json;

namespace LeafLedger.Modules.Ledger.Tests.Fixtures;

public sealed record LedgerCorePostingFixture(string Id, string Unit, string File, string Json)
{
    public override string ToString() => $"{Id} ({Unit}, {File})";
}

public static class LedgerCorePostingFixtureLoader
{
    private static readonly string FixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ledger-core");

    private static readonly HashSet<string> SupportedUnits =
    [
        "assertPostingAccountsValid",
        "assertPostingBusinessPartnersValid",
        "assertPostingUsersValid",
        "assertPostingProjectsValid",
        "getEffectivePeriodState",
        "assertPostingPeriodOpen",
        "updatePeriodState",
        "getPeriodForDate",
    ];

    public static IReadOnlyList<LedgerCorePostingFixture> LoadSelected()
    {
        var manifestPath = Path.Combine(FixtureRoot, "manifest.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));

        var cases = manifest.RootElement.GetProperty("cases")
            .EnumerateArray()
            .Where(item => IsSelected(item.GetProperty("file").GetString()!))
            .Select(Load)
            .ToArray();

        var duplicateIds = cases.GroupBy(item => item.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateIds.Length > 0)
        {
            throw new InvalidDataException($"Duplicate fixture ids: {string.Join(", ", duplicateIds)}");
        }

        return cases;
    }

    private static bool IsSelected(string file) =>
        file.StartsWith("posting-accounts/", StringComparison.Ordinal) ||
        file.StartsWith("posting-business-partners/", StringComparison.Ordinal) ||
        file.StartsWith("posting-users/", StringComparison.Ordinal) ||
        file.StartsWith("posting-projects/", StringComparison.Ordinal) ||
        file.StartsWith("period-state/", StringComparison.Ordinal) ||
        file.StartsWith("period-lifecycle/", StringComparison.Ordinal);

    private static LedgerCorePostingFixture Load(JsonElement item)
    {
        var id = item.GetProperty("id").GetString()!;
        var unit = item.GetProperty("unit").GetString()!;
        var file = item.GetProperty("file").GetString()!;
        if (!SupportedUnits.Contains(unit))
        {
            throw new InvalidDataException($"Unsupported selected fixture unit '{unit}' ({id}, {file}).");
        }

        var path = Path.Combine(FixtureRoot, file.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Fixture file does not exist ({id}, {unit}, {file}).", path);
        }

        return new LedgerCorePostingFixture(id, unit, file, File.ReadAllText(path));
    }
}

internal sealed class FixtureIds
{
    private readonly Dictionary<long, Guid> _forward = [];
    private readonly Dictionary<Guid, long> _reverse = [];

    public Guid Get(long value)
    {
        if (_forward.TryGetValue(value, out var id))
        {
            return id;
        }

        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes[8..], value);
        id = new Guid(bytes);
        _forward.Add(value, id);
        _reverse.Add(id, value);
        return id;
    }

    public long Reverse(Guid id) => _reverse[id];
}

internal static class FixtureJson
{
    public static DateOnly Date(JsonElement element, string property) =>
        DateOnly.ParseExact(element.GetProperty(property).GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateOnly? OptionalDate(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
            ? DateOnly.ParseExact(value.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;

    public static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() : null;
}
