using Microsoft.EntityFrameworkCore;

namespace Lassommoir.Infrastructure.Persistence;

public class LassommoirDbContext(DbContextOptions<LassommoirDbContext> options) : DbContext(options);
