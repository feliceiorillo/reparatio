using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Reparatio.Repairs.Infrastructure;

namespace Reparatio.Repairs.Api;

public sealed class IdentityStore(DbContextOptions<IdentityStore> options) : IdentityDbContext<IdentityUser>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("identity");
        builder.UseOpenIddict();
    }
}

public sealed class IdentityStoreDesignFactory : IDesignTimeDbContextFactory<IdentityStore>
{
    public IdentityStore CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<IdentityStore>()
        .UseSqlServer(SqlConnectionSettings.ReadRequired(), x => x.MigrationsHistoryTable("__IdentityMigrations", "identity"))
        .UseOpenIddict().Options);
}
