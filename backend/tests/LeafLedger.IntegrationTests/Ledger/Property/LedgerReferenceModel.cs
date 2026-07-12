namespace LeafLedger.IntegrationTests.Ledger.FinancialProperties;

internal sealed class LedgerReferenceModel
{
    private readonly Dictionary<Guid, long> _accountBalances = [];
    private readonly HashSet<string> _idempotencyKeys = [];
    private readonly HashSet<string> _reversedPosts = [];
    private readonly List<LedgerCommand.PostValid> _posts = [];
    private int _postCount;

    public long TotalBalanceMinor => _accountBalances.Values.Sum();

    public int LiveEntryCount => _postCount - _reversedPosts.Count;

    public IReadOnlyDictionary<Guid, long> AccountBalances => _accountBalances;

    public bool Apply(LedgerCommand command)
    {
        switch (command)
        {
            case LedgerCommand.PostValid post when _idempotencyKeys.Add(post.IdempotencyKey):
                AddBalance(post.DebitAccountId, post.AmountMinor);
                AddBalance(post.CreditAccountId, -post.AmountMinor);
                _posts.Add(post);
                _postCount++;
                return true;
            case LedgerCommand.Retry retry when !retry.ChangePayload:
                return _idempotencyKeys.Contains(retry.Original.IdempotencyKey);
            default:
                return false;
        }
    }

    public bool ApplyReverse(LedgerCommand.PostValid original)
    {
        if (!_reversedPosts.Add(original.IdempotencyKey))
        {
            return false;
        }

        AddBalance(original.DebitAccountId, -original.AmountMinor);
        AddBalance(original.CreditAccountId, original.AmountMinor);
        return true;
    }

    private void AddBalance(Guid accountId, long amountMinor)
    {
        _accountBalances[accountId] = _accountBalances.GetValueOrDefault(accountId) + amountMinor;
    }
}