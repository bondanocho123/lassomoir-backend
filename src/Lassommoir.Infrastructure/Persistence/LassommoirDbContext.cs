using Lassommoir.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;

namespace Lassommoir.Infrastructure.Persistence;

public class LassommoirDbContext(DbContextOptions<LassommoirDbContext> options) : DbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Guid>().HaveConversion<GuidToTextConverter>();
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeToTextConverter>();
    }
}
