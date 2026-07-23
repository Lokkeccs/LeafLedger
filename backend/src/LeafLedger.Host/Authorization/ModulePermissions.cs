namespace LeafLedger.Host.Authorization;

public static class ModulePermissions
{
    public const string PostLedger = "ledger.post";
    public const string ReverseLedger = "ledger.reverse";
    public const string ReadLedger = "ledger.read";
    public const string ClosePeriod = "period.close";
    public const string ManagePeriods = "period.manage";
    public const string ManageMembers = "members.manage";
    public const string ManageAccounts = "accounts.manage";

    public static bool Allows(SpaceRole role, string permission) =>
        role switch
        {
            SpaceRole.Owner or SpaceRole.Admin => IsKnownPermission(permission),
            SpaceRole.Member => permission is PostLedger or ReverseLedger or ReadLedger,
            SpaceRole.Viewer => permission is ReadLedger,
            _ => false,
        };

    private static bool IsKnownPermission(string permission) =>
        permission is PostLedger or ReverseLedger or ReadLedger or ClosePeriod or ManagePeriods or ManageMembers or ManageAccounts;
}