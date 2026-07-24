using System.Text.Json;
using Xunit;

namespace LeafLedger.IntegrationTests.Ledger;

[Trait("Category", "Contract")]
public sealed class AccountManagementContractTests
{
    private static readonly string[] WriteOperationIds =
    [
        "CreateAccount",
        "UpdateAccount",
        "ActivateAccount",
        "DeactivateAccount",
        "CreateAccountGroup",
        "UpdateAccountGroup",
    ];

    private static readonly string[] ImportExportOperationIds =
    [
        "ExportAccounts",
        "ExportAccountGroups",
        "ImportAccounts",
        "ImportAccountGroups",
    ];

    private static readonly string[] ImportRequestSchemaRefs =
    [
        "#/components/schemas/AccountImportRequest",
        "#/components/schemas/GroupImportRequest",
    ];

    [Fact]
    public void All_account_management_writes_expose_idempotency_and_authorization_contracts()
    {
        var contractPath = FindRepositoryFile("backend", "openapi", "leafledger-v1.json");
        using var document = JsonDocument.Parse(File.ReadAllText(contractPath));
        var operations = document.RootElement.GetProperty("paths")
            .EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(property => property.Name is "post" or "patch")
                .Select(property => property.Value))
            .Where(operation => WriteOperationIds.Contains(
                operation.GetProperty("operationId").GetString()!, StringComparer.Ordinal))
            .ToArray();

        Assert.Equal(WriteOperationIds.Length, operations.Length);
        foreach (var operation in operations)
        {
            Assert.Contains(
                operation.GetProperty("parameters").EnumerateArray(),
                parameter => parameter.GetProperty("name").GetString() == "Idempotency-Key" &&
                    parameter.GetProperty("in").GetString() == "header" &&
                    parameter.GetProperty("required").GetBoolean());
            Assert.True(operation.GetProperty("responses").TryGetProperty("401", out _));
            Assert.True(operation.GetProperty("responses").TryGetProperty("403", out _));
        }
    }

    [Fact]
    public void Account_import_export_operations_are_present_and_imports_require_idempotency()
    {
        var contractPath = FindRepositoryFile("backend", "openapi", "leafledger-v1.json");
        using var document = JsonDocument.Parse(File.ReadAllText(contractPath));
        var operations = document.RootElement.GetProperty("paths")
            .EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(property => property.Name is "get" or "post")
                .Select(property => property.Value))
            .Where(operation => ImportExportOperationIds.Contains(
                operation.GetProperty("operationId").GetString()!, StringComparer.Ordinal))
            .ToArray();

        Assert.Equal(ImportExportOperationIds.Length, operations.Length);
        foreach (var operation in operations)
        {
            Assert.True(operation.GetProperty("responses").TryGetProperty("401", out _));
            Assert.True(operation.GetProperty("responses").TryGetProperty("403", out _));
            if (operation.GetProperty("operationId").GetString()!.StartsWith("Import", StringComparison.Ordinal))
            {
                Assert.Contains(
                    operation.GetProperty("parameters").EnumerateArray(),
                    parameter => parameter.GetProperty("name").GetString() == "Idempotency-Key" &&
                        parameter.GetProperty("in").GetString() == "header" &&
                        parameter.GetProperty("required").GetBoolean());
                var content = operation.GetProperty("requestBody").GetProperty("content");
                Assert.True(content.TryGetProperty("text/csv", out _));
                var jsonSchema = content.GetProperty("application/json").GetProperty("schema");
                var requestSchema = jsonSchema.GetProperty("$ref").GetString();
                Assert.Contains(requestSchema, ImportRequestSchemaRefs);

                var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
                var rowSchema = operation.GetProperty("operationId").GetString() == "ImportAccounts"
                    ? schemas.GetProperty("AccountImportRow")
                    : schemas.GetProperty("GroupImportRow");
                Assert.Contains(rowSchema.GetProperty("properties").EnumerateObject(),
                    property => property.Name is "code" or "rangeStart");
            }
        }
    }

    private static string FindRepositoryFile(params string[] path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = path.Aggregate(directory.FullName, Path.Combine);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate the repository contract.", Path.Combine(path));
    }
}
