using Xunit;

namespace LeafLedger.IntegrationTests.Ledger.FinancialProperties;

internal sealed record PropertyCase<T>(ulong Seed, T Value);

internal static class PropertyRunner
{
    public static void Run<T>(
        string propertyName,
        int iterations,
        ulong seed,
        Func<Random, T> generate,
        Func<T, bool> succeeds,
        Func<T, IEnumerable<T>> shrink)
    {
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var caseSeed = seed + (ulong)iteration;
            var value = generate(new Random(unchecked((int)caseSeed)));
            if (succeeds(value))
            {
                continue;
            }

            var minimal = Minimize(value, succeeds, shrink);
            Assert.Fail($"{propertyName} failed. Seed: {caseSeed}. Counterexample: {minimal}");
        }
    }

    public static async Task RunAsync<T>(
        string propertyName,
        int iterations,
        ulong seed,
        Func<Random, T> generate,
        Func<T, Task<bool>> succeeds,
        Func<T, IEnumerable<T>> shrink)
    {
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var caseSeed = seed + (ulong)iteration;
            var value = generate(new Random(unchecked((int)caseSeed)));
            if (await succeeds(value))
            {
                continue;
            }

            var minimal = value;
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var candidate in shrink(minimal))
                {
                    if (await succeeds(candidate))
                    {
                        continue;
                    }

                    minimal = candidate;
                    changed = true;
                    break;
                }
            }

            Assert.Fail($"{propertyName} failed. Seed: {caseSeed}. Counterexample: {minimal}");
        }
    }

    public static string DeterministicKey(ulong value)
    {
        var bytes = new byte[16];
        BitConverter.TryWriteBytes(bytes.AsSpan(8), value);
        return new Ulid(bytes).ToString();
    }

    private static T Minimize<T>(T value, Func<T, bool> succeeds, Func<T, IEnumerable<T>> shrink)
    {
        var current = value;
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var candidate in shrink(current))
            {
                if (succeeds(candidate))
                {
                    continue;
                }

                current = candidate;
                changed = true;
                break;
            }
        }

        return current;
    }
}

[Trait("Category", "Property")]
public sealed class PropertyRunnerTests
{
    [Fact]
    public void Runner_is_seeded_and_shrinks_a_failing_value()
    {
        var exception = Record.Exception(() => PropertyRunner.Run(
                propertyName: "positive values",
                iterations: 32,
                seed: 0x5eedUL,
                generate: random => random.Next(-100, 101),
                succeeds: value => value >= 0,
            shrink: value => value == 0 ? [] : [value / 2]));

        Assert.NotNull(exception);
        Assert.Contains("Seed: 24301", exception!.Message);
        Assert.Contains("Counterexample: -1", exception.Message);
    }

    [Fact]
    public void Generated_valid_sequences_preserve_the_reference_balance_invariant()
    {
        var debitAccountId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var creditAccountId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        PropertyRunner.Run(
            propertyName: "reference balance",
            iterations: LedgerCommandGenerator.Iterations,
            seed: 0x1A2B3C4DUL,
            generate: random => LedgerCommandGenerator.GenerateSequence(random, debitAccountId, creditAccountId),
            succeeds: sequence =>
            {
                var model = new LedgerReferenceModel();
                foreach (var command in sequence)
                {
                    model.Apply(command);
                }

                return model.TotalBalanceMinor == 0;
            },
            shrink: LedgerCommandGenerator.ShrinkSequence);
    }
}