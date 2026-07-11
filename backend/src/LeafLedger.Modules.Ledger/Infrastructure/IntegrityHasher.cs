using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LeafLedger.Modules.Ledger.Infrastructure;

public sealed record IntegrityBalanceRow(Guid AccountId, int AccountCode, long BaseBalanceMinor);

public static class IntegrityHasher
{
    public const string Algorithm = "sha256";
    public const string Version = "trial-balance-v1";

    public static string Compute(Guid spaceId, IEnumerable<IntegrityBalanceRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var canonical = new StringBuilder()
            .Append(Version)
            .Append('\n')
            .Append(spaceId.ToString("D", CultureInfo.InvariantCulture));

        foreach (var row in rows.OrderBy(item => item.AccountCode).ThenBy(item => item.AccountId))
        {
            canonical.Append('\n')
                .Append(row.AccountCode.ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(row.AccountId.ToString("D", CultureInfo.InvariantCulture))
                .Append(':')
                .Append(row.BaseBalanceMinor.ToString(CultureInfo.InvariantCulture));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }
}