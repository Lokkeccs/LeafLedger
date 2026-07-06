using Testcontainers.PostgreSql;
using Npgsql;
using Xunit;

namespace LeafLedger.IntegrationTests;

[Trait("Category", "Integration")]
public class PostgresConnectionTests : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithDatabase("leafledger_test")
            .WithUsername("testuser")
            .WithPassword("testpassword")
            .Build();

        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
        }
    }

    [Fact]
    public async Task PostgresContainerShouldStartSuccessfully()
    {
        // Arrange
        Assert.NotNull(_container);
        var connectionString = _container!.GetConnectionString();

        // Act
        using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            using (var cmd = new NpgsqlCommand("SELECT 1", connection))
            {
                var result = await cmd.ExecuteScalarAsync();

                // Assert
                Assert.NotNull(result);
                Assert.Equal(1, result);
            }
        }
    }
}
