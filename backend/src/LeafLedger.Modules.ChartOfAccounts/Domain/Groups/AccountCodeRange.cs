using LeafLedger.SharedKernel;

namespace LeafLedger.Modules.ChartOfAccounts.Domain.Groups;

public readonly record struct AccountCodeRange
{
    private AccountCodeRange(int start, int end)
    {
        Start = start;
        End = end;
    }

    public int Start { get; }

    public int End { get; }

    public static Result<AccountCodeRange> Create(int start, int end) => start <= end
        ? Result<AccountCodeRange>.Success(new AccountCodeRange(start, end))
        : Result<AccountCodeRange>.Failure(new DomainError(
            "account_code_range.invalid",
            "Account-code range start must not be after its end."));

    public bool Contains(int code) => code >= Start && code <= End;
}
