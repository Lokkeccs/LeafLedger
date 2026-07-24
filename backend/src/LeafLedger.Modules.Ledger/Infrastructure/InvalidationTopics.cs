namespace LeafLedger.Modules.Ledger.Infrastructure;

public static class InvalidationTopics
{
    public const string TrialBalance = "reports.trialBalance";
    public const string JournalEntries = "journalEntries.list";
    public const string Accounts = "accounts.list";
    public const string AccountGroups = "accountGroups.list";

    public static readonly IReadOnlyList<string> PostingTopics =
        new[] { TrialBalance, JournalEntries };

    public static readonly IReadOnlyList<string> AccountCatalogTopics =
        new[] { Accounts, AccountGroups };

    public static bool IsKnown(string topic) =>
        topic is TrialBalance or JournalEntries or Accounts or AccountGroups;
}
