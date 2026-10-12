using Lassommoir.Infrastructure;
using Lassommoir.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lassommoir.Tests;

public class SqlitePragmaTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"lassommoir-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = _dbPath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private ServiceProvider BuildProvider(int? busyTimeoutMs = null)
    {
        var settings = new Dictionary<string, string?> { ["ConnectionStrings:Default"] = $"Data Source={_dbPath}" };
        if (busyTimeoutMs is not null) settings["Database:BusyTimeoutMs"] = busyTimeoutMs.ToString();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddPersistence(configuration);
        return services.BuildServiceProvider();
    }

    private static long Scalar(LassommoirDbContext context, string sql)
    {
        var connection = context.Database.GetDbConnection();
        context.Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    [Fact]
    public void NewConnection_HasWalBusyTimeoutAndForeignKeys()
    {
        using var provider = BuildProvider(1234);
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LassommoirDbContext>();

        var connection = context.Database.GetDbConnection();
        context.Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("wal", command.ExecuteScalar()?.ToString());

        Assert.Equal(1234, Scalar(context, "PRAGMA busy_timeout;"));
        Assert.Equal(1, Scalar(context, "PRAGMA foreign_keys;"));
    }

    [Fact]
    public void BusyTimeout_DefaultsTo5000()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LassommoirDbContext>();

        Assert.Equal(5000, Scalar(context, "PRAGMA busy_timeout;"));
    }

    [Fact]
    public async Task TwoParallelWriters_DoNotThrowBusy()
    {
        using var provider = BuildProvider();
        using (var scope = provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<LassommoirDbContext>();
            await context.Database.ExecuteSqlRawAsync("CREATE TABLE items (id INTEGER PRIMARY KEY, value TEXT NOT NULL);");
        }

        async Task WriteAsync()
        {
            using var scope = provider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<LassommoirDbContext>();
            for (var i = 0; i < 50; i++)
            {
                await context.Database.ExecuteSqlRawAsync("INSERT INTO items (value) VALUES ('x');");
            }
        }

        await Task.WhenAll(Task.Run(WriteAsync), Task.Run(WriteAsync));

        using var verifyScope = provider.CreateScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<LassommoirDbContext>();
        Assert.Equal(100, Scalar(verify, "SELECT COUNT(*) FROM items;"));
    }
}
