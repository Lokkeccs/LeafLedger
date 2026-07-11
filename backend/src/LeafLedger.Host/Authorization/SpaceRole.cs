namespace LeafLedger.Host.Authorization;

public enum SpaceRole
{
    Owner,
    Admin,
    Member,
    Viewer,
}

public static class SpaceRoleParser
{
    public static bool TryParse(string? value, out SpaceRole role)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "owner":
                role = SpaceRole.Owner;
                return true;
            case "admin":
                role = SpaceRole.Admin;
                return true;
            case "member":
                role = SpaceRole.Member;
                return true;
            case "viewer":
                role = SpaceRole.Viewer;
                return true;
            default:
                role = default;
                return false;
        }
    }
}