using Lassommoir.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace Lassommoir.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default belum dikonfigurasi.");

        var busyTimeoutMs = configuration.GetValue("Database:BusyTimeoutMs", 5000);

        services.AddDbContext<LassommoirDbContext>(options => options
            .UseSqlite(connectionString)
            .AddInterceptors(new SqlitePragmaInterceptor(busyTimeoutMs)));

        return services;
    }
}
