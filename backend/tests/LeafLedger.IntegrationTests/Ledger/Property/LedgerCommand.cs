namespace LeafLedger.IntegrationTests.Ledger.FinancialProperties;

internal abstract record LedgerCommand
{
    public sealed record PostValid(
        DateOnly Date,
        Guid DebitAccountId,
        Guid CreditAccountId,
        long AmountMinor,
        string IdempotencyKey) : LedgerCommand;

    public sealed record PostUnbalanced(
        DateOnly Date,
        Guid AccountId,
        long DebitAmountMinor,
        long CreditAmountMinor,
        string IdempotencyKey) : LedgerCommand;

    public sealed record Reverse(int OriginalPostIndex, DateOnly Date, string IdempotencyKey) : LedgerCommand;

    public sealed record InvalidReference(
        DateOnly Date,
        Guid AccountId,
        long AmountMinor,
        string IdempotencyKey) : LedgerCommand;

    public sealed record Retry(PostValid Original, bool ChangePayload) : LedgerCommand;
}

internal static class LedgerCommandGenerator
{
    public const int Iterations = 12;
    public const int MaxSequenceLength = 8;

    public static IReadOnlyList<LedgerCommand> GenerateSequence(
        Random random,
        Guid debitAccountId,
        Guid creditAccountId)
    {
        var commands = new List<LedgerCommand>();
        var sequenceLength = random.Next(1, MaxSequenceLength + 1);

        var postCount = 0;
        for (var remaining = sequenceLength; remaining > 0; remaining--)
        {
            var amount = random.Next(1, 1001);
            var post = new LedgerCommand.PostValid(
                new DateOnly(2026, 1, 1).AddDays(random.Next(0, 365)),
                debitAccountId,
                creditAccountId,
                amount,
                NewDeterministicUlid(random));
            commands.Add(post);
            postCount++;

            if (random.Next(0, 3) == 0)
            {
                commands.Add(new LedgerCommand.Retry(post, ChangePayload: false));
            }

            if (random.Next(0, 2) == 0)
            {
                commands.Add(new LedgerCommand.Reverse(
                    postCount - 1,
                    post.Date,
                    NewDeterministicUlid(random)));
            }
        }

        return commands;
    }

    private static string NewDeterministicUlid(Random random)
    {
        var bytes = new byte[16];
        random.NextBytes(bytes);
        bytes[0] &= 0x3f;
        return new Ulid(bytes).ToString();
    }

    public static IEnumerable<IReadOnlyList<LedgerCommand>> ShrinkSequence(IReadOnlyList<LedgerCommand> sequence)
    {
        for (var index = 0; index < sequence.Count; index++)
        {
            var candidate = sequence.Where((_, candidateIndex) => candidateIndex != index).ToArray();
            if (candidate.Length > 0)
            {
                yield return candidate;
            }
        }
    }
}