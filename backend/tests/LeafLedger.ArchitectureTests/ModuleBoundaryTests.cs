using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace LeafLedger.ArchitectureTests;

/// <summary>
/// Boundary rules from the target architecture (Part 3 §5):
/// - SharedKernel references no other LeafLedger assembly.
/// - *.Domain namespaces depend on nothing but SharedKernel.
/// - EF Core is confined to *.Infrastructure namespaces.
/// Trivially green on the skeleton; they bite as modules appear.
/// </summary>
public class ModuleBoundaryTests
{
    private static Assembly[] AllLeafLedgerAssemblies() =>
    [
        Assembly.Load("LeafLedger.SharedKernel"),
        Assembly.Load("LeafLedger.Host"),
        Assembly.Load("LeafLedger.Modules.ChartOfAccounts"),
        Assembly.Load("LeafLedger.Modules.Ledger"),
    ];

    [Fact]
    public void SharedKernelDependsOnNoOtherLeafLedgerAssembly()
    {
        var result = Types
            .InAssembly(Assembly.Load("LeafLedger.SharedKernel"))
            .ShouldNot()
            .HaveDependencyOnAny("LeafLedger.Host", "LeafLedger.Modules")
            .GetResult();

        Assert.True(result.IsSuccessful, FailingTypes(result));
    }

    [Fact]
    public void DomainNamespacesDependOnlyOnSharedKernel()
    {
        var chartOfAccountsResult = Types
            .InAssembly(Assembly.Load("LeafLedger.Modules.ChartOfAccounts"))
            .That()
            .ResideInNamespaceMatching(@"\.Domain($|\.)")
            .ShouldNot()
            .HaveDependencyOnAny(
                "LeafLedger.Host",
                "LeafLedger.Modules.Ledger",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore")
            .GetResult();

        var ledgerResult = Types
            .InAssembly(Assembly.Load("LeafLedger.Modules.Ledger"))
            .That()
            .ResideInNamespaceMatching(@"\.Domain($|\.)")
            .ShouldNot()
            .HaveDependencyOnAny(
                "LeafLedger.Host",
                "LeafLedger.Modules.ChartOfAccounts",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore")
            .GetResult();

        Assert.True(chartOfAccountsResult.IsSuccessful, FailingTypes(chartOfAccountsResult));
        Assert.True(ledgerResult.IsSuccessful, FailingTypes(ledgerResult));
    }

    [Fact]
    public void EfCoreIsConfinedToInfrastructureNamespaces()
    {
        var result = Types
            .InAssemblies(AllLeafLedgerAssemblies())
            .That()
            .DoNotResideInNamespaceMatching(@"\.Infrastructure($|\.)")
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        Assert.True(result.IsSuccessful, FailingTypes(result));
    }

    private static string FailingTypes(TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : "Violations: " + string.Join(", ", result.FailingTypeNames ?? []);
}
