namespace LeafLedger.Host.Authorization;

public static class ModulePermissions
{
    public const string PostLedger = "ledger.post";
    public const string ReverseLedger = "ledger.reverse";
    public const string ClosePeriod = "period.close";
    public const string ManageMembers = "members.manage";

    public static bool Allows(SpaceRole role, string permission) =>
        role switch
        {
            SpaceRole.Owner or SpaceRole.Admin => IsKnownPermission(permission),
            SpaceRole.Member => permission is PostLedger or ReverseLedger,
            SpaceRole.Viewer => false,
            _ => false,
        };

    private static bool IsKnownPermission(string permission) =>
        permission is PostLedger or ReverseLedger or ClosePeriod or ManageMembers;
}