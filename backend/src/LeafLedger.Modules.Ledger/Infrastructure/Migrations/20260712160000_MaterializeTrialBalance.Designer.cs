using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeafLedger.Modules.Ledger.Infrastructure.Migrations;

[DbContext(typeof(LedgerDbContext))]
[Migration("20260712160000_MaterializeTrialBalance")]
partial class MaterializeTrialBalance
{
}