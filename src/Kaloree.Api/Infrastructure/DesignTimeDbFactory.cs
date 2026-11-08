using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kaloree.Api.Infrastructure;

public sealed class DesignTimeDbFactory : IDesignTimeDbContextFactory<AppDb>
{
    public AppDb CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<AppDb>()
        .UseNpgsql("Host=localhost;Database=kaloree;Username=kaloree;Password=design-only").Options);
}
