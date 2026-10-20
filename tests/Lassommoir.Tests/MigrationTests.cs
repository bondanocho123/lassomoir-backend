using Lassommoir.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Lassommoir.Tests;

public class MigrationTests : IDisposable
{
    private static readonly string[] Tables = ["Devices", "MobileClients", "Projects", "Tasks", "StageRuns", "TaskEvents", "Commands"];

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

    private WebApplicationFactory<Program> CreateFactory(string environment, bool? migrateOnStartup)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:Default", $"Data Source={_dbPath}");
            if (migrateOnStartup is not null)
            {
                builder.UseSetting("Database:MigrateOnStartup", migrateOnStartup.Value.ToString());
            }
        });
    }

    private static List<string> GetObjectNames(LassommoirDbContext context, string type)
    {
        var connection = context.Database.GetDbConnection();
        context.Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name FROM sqlite_master WHERE type = '{type}'";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));
        return names;
    }

    [Fact]
    public void Startup_WithMigrateEnabled_CreatesTablesAndTriggers()
    {
        using var factory = CreateFactory("Production", true);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LassommoirDbContext>();

        var tables = GetObjectNames(context, "table");
        foreach (var table in Tables)
            Assert.Contains(table, tables);
        Assert.DoesNotContain("Reviews", tables);

        var triggers = GetObjectNames(context, "trigger");
        Assert.Contains("TaskEvents_no_update", triggers);
        Assert.Contains("TaskEvents_no_delete", triggers);
    }

    [Fact]
    public void Development_WithoutExplicitConfig_Migrates()
    {
        using var factory = CreateFactory("Development", null);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LassommoirDbContext>();

        Assert.Contains("TaskEvents", GetObjectNames(context, "table"));
    }

    [Fact]
    public void NonDevelopment_WithoutExplicitConfig_DoesNotMigrate()
    {
        using var factory = CreateFactory("Production", null);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LassommoirDbContext>();

        Assert.DoesNotContain("TaskEvents", GetObjectNames(context, "table"));
    }

    [Fact]
    public void Development_WithMigrateDisabled_DoesNotMigrate()
    {
        using var factory = CreateFactory("Development", false);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LassommoirDbContext>();

        Assert.DoesNotContain("TaskEvents", GetObjectNames(context, "table"));
    }

    private static LassommoirDbContext SeedTaskEvent(IServiceScope scope)
    {
        var context = scope.ServiceProvider.GetRequiredService<LassommoirDbContext>();
        context.Database.ExecuteSqlRaw("INSERT INTO Devices (Id, Name, TokenHash, CreatedAt) VALUES ('d1', 'dev', 'h1', 't')");
        context.Database.ExecuteSqlRaw("INSERT INTO Projects (Id, DeviceId, LocalId, Title) VALUES ('p1', 'd1', 'spacewar', 'spacewar')");
        context.Database.ExecuteSqlRaw(
            "INSERT INTO Tasks (Id, ProjectId, LocalId, Stage, Category, Title, Subtext, Attachments, State, RunState, CreatedAt, UpdatedAt) " +
            "VALUES ('t1', 'p1', '1791808370495', 'WAITING', '', 'x', '', '[]', 'Idle', 'Idle', 't', 't')");
        context.Database.ExecuteSqlRaw("INSERT INTO TaskEvents (Id, TaskId, Type, CreatedAt) VALUES ('e1', 't1', 'created', 't')");
        return context;
    }

    [Fact]
    public void TaskEvents_AllowInsert_RejectUpdateAndDelete()
    {
        using var factory = CreateFactory("Production", true);
        using var scope = factory.Services.CreateScope();
        var context = SeedTaskEvent(scope);

        var update = Assert.ThrowsAny<Exception>(() => context.Database.ExecuteSqlRaw("UPDATE TaskEvents SET Type = 'x' WHERE Id = 'e1'"));
        Assert.Contains("append-only", update.ToString());
        var delete = Assert.ThrowsAny<Exception>(() => context.Database.ExecuteSqlRaw("DELETE FROM TaskEvents WHERE Id = 'e1'"));
        Assert.Contains("append-only", delete.ToString());
    }

    [Fact]
    public void DeletingTaskWithEvents_IsRejectedByForeignKey()
    {
        using var factory = CreateFactory("Production", true);
        using var scope = factory.Services.CreateScope();
        var context = SeedTaskEvent(scope);

        var ex = Assert.ThrowsAny<Exception>(() => context.Database.ExecuteSqlRaw("DELETE FROM Tasks WHERE Id = 't1'"));
        Assert.Contains("FOREIGN KEY", ex.ToString());
    }

    [Fact]
    public void ProjectLocalId_IsUniquePerDevice()
    {
        using var factory = CreateFactory("Production", true);
        using var scope = factory.Services.CreateScope();
        var context = SeedTaskEvent(scope);
        context.Database.ExecuteSqlRaw("INSERT INTO Devices (Id, Name, TokenHash, CreatedAt) VALUES ('d2', 'dev2', 'h2', 't')");

        var ex = Assert.ThrowsAny<Exception>(() => context.Database.ExecuteSqlRaw(
            "INSERT INTO Projects (Id, DeviceId, LocalId, Title) VALUES ('p2', 'd1', 'spacewar', 'lagi')"));
        Assert.Contains("UNIQUE", ex.ToString());

        context.Database.ExecuteSqlRaw("INSERT INTO Projects (Id, DeviceId, LocalId, Title) VALUES ('p3', 'd2', 'spacewar', 'spacewar')");
    }

    [Fact]
    public void TaskLocalId_IsUniquePerProject()
    {
        using var factory = CreateFactory("Production", true);
        using var scope = factory.Services.CreateScope();
        var context = SeedTaskEvent(scope);
        context.Database.ExecuteSqlRaw("INSERT INTO Projects (Id, DeviceId, LocalId, Title) VALUES ('p2', 'd1', 'TTT', 'TTT')");
        const string insertTask =
            "INSERT INTO Tasks (Id, ProjectId, LocalId, Stage, Category, Title, Subtext, Attachments, State, RunState, CreatedAt, UpdatedAt) " +
            "VALUES ({0}, {1}, '1791808370495', 'WAITING', '', 'x', '', '[]', 'Idle', 'Idle', 't', 't')";

        var ex = Assert.ThrowsAny<Exception>(() => context.Database.ExecuteSqlRaw(insertTask, "t2", "p1"));
        Assert.Contains("UNIQUE", ex.ToString());

        context.Database.ExecuteSqlRaw(insertTask, "t3", "p2");
    }

    [Fact]
    public void Down_RemovesTriggersAndTables()
    {
        using var factory = CreateFactory("Production", true);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LassommoirDbContext>();

        context.GetService<IMigrator>().Migrate("0");

        Assert.Empty(GetObjectNames(context, "trigger"));
        var tables = GetObjectNames(context, "table");
        foreach (var table in Tables.Append("Reviews"))
            Assert.DoesNotContain(table, tables);
    }

    [Fact]
    public void Model_HasNoPendingChanges()
    {
        using var factory = CreateFactory("Production", false);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LassommoirDbContext>();

        Assert.False(context.Database.HasPendingModelChanges());
    }
}
