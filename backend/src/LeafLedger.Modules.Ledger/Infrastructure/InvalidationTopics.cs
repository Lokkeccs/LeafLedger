namespace LeafLedger.Modules.Ledger.Infrastructure;

public static class InvalidationTopics
{
    public const string TrialBalance = "reports.trialBalance";
    public const string JournalEntries = "journalEntries.list";

    public static readonly IReadOnlyList<string> PostingTopics =
        new[] { TrialBalance, JournalEntries };

    public static bool IsKnown(string topic) =>
        topic is TrialBalance or JournalEntries;
}
