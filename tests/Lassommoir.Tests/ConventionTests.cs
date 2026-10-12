using Lassommoir.Domain;
using Lassommoir.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Lassommoir.Tests;

public class ConventionTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public ConventionTests()
    {
        _connection.Open();
        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private ConventionDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<LassommoirDbContext>().UseSqlite(_connection).Options);

    [Fact]
    public void Guid_And_DateTime_RoundTrip_Identically()
    {
        var id = Ids.NewId();
        var at = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc).AddTicks(1234567);

        using (var context = CreateContext())
        {
            context.Items.Add(new Item { Id = id, At = at, MaybeAt = at });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var item = context.Items.Single();
            Assert.Equal(id, item.Id);
            Assert.Equal(at, item.At);
            Assert.Equal(DateTimeKind.Utc, item.At.Kind);
            Assert.Equal(at, item.MaybeAt);
            Assert.Equal(DateTimeKind.Utc, item.MaybeAt!.Value.Kind);
        }
    }

    [Fact]
    public void Guid_IsStoredAsLowercaseText_AndIsVersion7()
    {
        var id = Ids.NewId();
        using (var context = CreateContext())
        {
            context.Items.Add(new Item { Id = id, At = DateTime.UtcNow });
            context.SaveChanges();
        }

        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT Id, typeof(Id) FROM Items";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(id.ToString("D"), reader.GetString(0));
        Assert.Equal("text", reader.GetString(1));
        Assert.Equal(7, id.Version);
    }

    [Fact]
    public void LocalDateTime_IsStoredAsEquivalentUtc()
    {
        var local = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Local);
        var id = Ids.NewId();

        using (var context = CreateContext())
        {
            context.Items.Add(new Item { Id = id, At = local });
            context.SaveChanges();
        }

        using var read = CreateContext();
        var loaded = read.Items.Single(i => i.Id == id);
        Assert.Equal(DateTimeKind.Utc, loaded.At.Kind);
        Assert.Equal(local.ToUniversalTime(), loaded.At);
    }

    [Fact]
    public void OrderByAndWhere_OnDateTime_RunInDatabaseAndSortCorrectly()
    {
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var offsets = new[] { 5, 1, 3, 2, 4 };

        using (var context = CreateContext())
        {
            foreach (var offset in offsets)
                context.Items.Add(new Item { Id = Ids.NewId(), At = baseTime.AddDays(offset).AddTicks(offset) });
            context.SaveChanges();
        }

        using var read = CreateContext();
        var cutoff = baseTime.AddDays(2);

        var query = read.Items.Where(i => i.At > cutoff).OrderBy(i => i.At);
        var sql = query.ToQueryString();
        Assert.Contains("WHERE", sql);
        Assert.Contains("ORDER BY", sql);

        var result = query.Select(i => i.At).ToList();
        Assert.Equal(
            new[] { 3, 4, 5 }.Select(o => baseTime.AddDays(o).AddTicks(o)),
            result);
    }

    private class Item
    {
        public Guid Id { get; set; }
        public DateTime At { get; set; }
        public DateTime? MaybeAt { get; set; }
    }

    private class ConventionDbContext(DbContextOptions<LassommoirDbContext> options) : LassommoirDbContext(options)
    {
        public DbSet<Item> Items => Set<Item>();
    }
}
