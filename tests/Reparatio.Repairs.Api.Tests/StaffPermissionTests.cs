using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Reparatio.Repairs.Api;
using Reparatio.Repairs.Domain;
using Reparatio.Repairs.Infrastructure;
using Xunit;

namespace Reparatio.Repairs.Api.Tests;

public sealed class StaffPermissionTests
{
    [SqlTheory]
    [InlineData(StaffRole.TenantAdministrator, true, true, true)]
    [InlineData(StaffRole.SiteManager, true, true, true)]
    [InlineData(StaffRole.Receptionist, true, false, false)]
    [InlineData(StaffRole.Technician, false, false, true)]
    public async Task Scope_assignment_and_database_role_control_access(StaffRole role, bool receive, bool manage, bool test)
    {
        var factory = new RepairsDbContextFactory(SqlConnectionSettings.ReadRequired());
        var tenant = Guid.NewGuid(); var site = Guid.NewGuid(); var actor = Guid.NewGuid(); var tech = Guid.NewGuid(); var repair = Guid.NewGuid();
        var subject = Guid.NewGuid().ToString(); const string issuer = "https://localhost:7240/";
        await using var db = factory.CreateDbContext();
        db.Sites.Add(new() { TenantId = tenant, SiteId = site });
        db.Technicians.Add(new() { TenantId = tenant, SiteId = site, Id = tech, IsAvailable = true });
        db.Repairs.Add(new() { TenantId = tenant, SiteId = site, Id = repair, TechnicianId = tech, Status = RepairStatus.AwaitingTesting, ArrivalSequence = 1 });
        db.StaffIdentities.Add(new() { Id = actor, Issuer = issuer, Subject = subject });
        db.StaffGrants.Add(new() { Id = Guid.NewGuid(), UserId = actor, TenantId = tenant, Role = role,
            SiteId = role == StaffRole.TenantAdministrator ? null : site, TechnicianId = role == StaffRole.Technician ? tech : null });
        await db.SaveChangesAsync();
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new(new ClaimsIdentity([
            new("iss", issuer), new("sub", subject), new("role", "TenantAdministrator") ], "validated")) } };
        var access = new StaffAccess(http, factory);
        try
        {
            await AssertPermission(() => access.EnsureCanReceiveAsync(tenant, site, default), receive);
            await AssertPermission(() => access.EnsureCanManageAsync(tenant, site, default), manage);
            await AssertPermission(async () => { Assert.Equal(actor, await access.EnsureCanTestAsync(tenant, site, repair, default)); }, test);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => access.EnsureCanReadAsync(Guid.NewGuid(), site, repair, default));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => access.EnsureCanReadAsync(tenant, Guid.NewGuid(), repair, default));
            await access.EnsureCanReadAsync(tenant, site, repair, default);
            if (role == StaffRole.Technician)
            {
                await db.Repairs.Where(x => x.TenantId == tenant).ExecuteUpdateAsync(s => s.SetProperty(x => x.TechnicianId, (Guid?)null).SetProperty(x => x.Status, RepairStatus.WaitingForAssignment));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => access.EnsureCanTestAsync(tenant, site, repair, default));
            }
            await db.StaffIdentities.Where(x => x.Id == actor).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => access.EnsureCanReadAsync(tenant, site, repair, default));
        }
        finally
        {
            await db.StaffGrants.Where(x => x.UserId == actor).ExecuteDeleteAsync();
            await db.StaffIdentities.Where(x => x.Id == actor).ExecuteDeleteAsync();
            await db.Repairs.Where(x => x.TenantId == tenant).ExecuteDeleteAsync();
            await db.Technicians.Where(x => x.TenantId == tenant).ExecuteDeleteAsync();
            await db.Sites.Where(x => x.TenantId == tenant).ExecuteDeleteAsync();
        }
    }

    private static async Task AssertPermission(Func<Task> action, bool permitted)
    {
        if (permitted) await action();
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(action);
    }
}
