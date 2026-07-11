using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LeafLedger.Modules.Ledger.Infrastructure.DesignTime;

/// <summary>
/// Design-time factory used only by the EF CLI (<c>dotnet ef</c>) to build the model for
/// migration scaffolding. It never opens a connection, so the connection string is a
/// non-secret placeholder.
/// </summary>
public sealed class LedgerDbContextFactory : IDesignTimeDbContextFactory<LedgerDbContext>
{
    public LedgerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<LedgerDbContext>()
            .UseNpgsql("Host=localhost;Database=leafledger_design")
            .Options;

        return new LedgerDbContext(options);
    }
}
