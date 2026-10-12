using Lassommoir.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Lassommoir.Tests;

public class PersistenceTests
{
    [Fact]
    public void DbContext_CanBeResolvedFromDi()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var scope = factory.Services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<LassommoirDbContext>();

        Assert.NotNull(context);
    }
}
