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
        var result = Types
            .InAssemblies(AllLeafLedgerAssemblies())
            .That()
            .ResideInNamespaceMatching(@"\.Domain($|\.)")
            .ShouldNot()
            .HaveDependencyOnAny(
                "LeafLedger.Host",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore")
            .GetResult();

        Assert.True(result.IsSuccessful, FailingTypes(result));
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
