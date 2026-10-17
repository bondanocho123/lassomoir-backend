using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lassommoir.Infrastructure.Persistence;

/// <summary>Dipakai `dotnet ef` tanpa menjalankan host. Memakai file lokal tetap.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<LassommoirDbContext>
{
    public LassommoirDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<LassommoirDbContext>()
            .UseSqlite("Data Source=lassommoir.design.db")
            .Options;

        return new LassommoirDbContext(options);
    }
}
