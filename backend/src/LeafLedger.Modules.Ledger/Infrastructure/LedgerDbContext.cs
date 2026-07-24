using System.Text;
using LeafLedger.Modules.Ledger.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace LeafLedger.Modules.Ledger.Infrastructure;

/// <summary>
/// The Phase-2 ledger-core persistence context. Owns the full schema baseline
/// (spaces / memberships / account groups / accounts / periods / journal entries /
/// journal lines / line attributions / audit log). Per decision D1 (P2-WP02) this is a
/// single context for the coupled Phase-2 tables; per-module contexts are introduced when
/// those modules gain domains (P2-WP03+).
/// </summary>
public sealed class LedgerDbContext : DbContext
{
    public LedgerDbContext(DbContextOptions<LedgerDbContext> options)
        : base(options)
    {
    }

    public DbSet<Space> Spaces => Set<Space>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<AccountGroup> AccountGroups => Set<AccountGroup>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Period> Periods => Set<Period>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalLine> JournalLines => Set<JournalLine>();
    public DbSet<LineAttribution> LineAttributions => Set<LineAttribution>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    public DbSet<BusinessPartner> BusinessPartners => Set<BusinessPartner>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LedgerDbContext).Assembly);

        // Deterministic snake_case column names so the raw DDL (RLS policies, triggers,
        // exclusion constraint) and the EF model agree on identifiers.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
    }

    private static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                {
                    sb.Append('_');
                }

                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
