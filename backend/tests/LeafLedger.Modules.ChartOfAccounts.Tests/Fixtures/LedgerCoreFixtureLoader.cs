using System.Text.Json;

namespace LeafLedger.Modules.ChartOfAccounts.Tests.Fixtures;

public sealed record LedgerCoreFixture(string Id, string Unit, string File, string Json)
{
    public override string ToString() => $"{Id} ({Unit}, {File})";
}

public static class LedgerCoreFixtureLoader
{
    private static readonly string FixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ledger-core");

    public static IReadOnlyList<LedgerCoreFixture> LoadSelected()
    {
        var manifestPath = Path.Combine(FixtureRoot, "manifest.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));

        var cases = manifest.RootElement.GetProperty("cases")
            .EnumerateArray()
            .Where(item => IsSelected(item.GetProperty("file").GetString()!))
            .Select(item => Load(item))
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
        file.StartsWith("currency-policy/", StringComparison.Ordinal) ||
        file.StartsWith("fx-metadata/", StringComparison.Ordinal);

    private static LedgerCoreFixture Load(JsonElement item)
    {
        var id = item.GetProperty("id").GetString()!;
        var unit = item.GetProperty("unit").GetString()!;
        var file = item.GetProperty("file").GetString()!;
        if (unit is not (
            "assertPostingCurrencyPolicyValid" or
            "resolveGroupFxPolicy" or
            "resolveAccountFxPolicy" or
            "buildTransactionLineFxMetadata"))
        {
            throw new InvalidDataException($"Unsupported selected fixture unit '{unit}' ({id}, {file}).");
        }

        var path = Path.Combine(FixtureRoot, file.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Fixture file does not exist ({id}, {unit}, {file}).", path);
        }

        return new LedgerCoreFixture(id, unit, file, File.ReadAllText(path));
    }
}
