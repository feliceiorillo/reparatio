using Microsoft.EntityFrameworkCore;
using Reparatio.Repairs.Application;
using Reparatio.Repairs.Infrastructure;

namespace Reparatio.Repairs.Api;

public sealed record SitePermission(Guid ActorId, bool AllRepairs, IReadOnlyList<Guid> TechnicianIds);
public sealed class RepairNotFoundException : Exception;
public sealed class StaffAccess(IHttpContextAccessor http, IDbContextFactory<RepairsDbContext> factory)
    : IRepairAccess, ITechnicianAvailabilityAccess, IRepairLifecycleAccess
{
    public async Task EnsureCanReceiveAsync(Guid tenantId, Guid siteId, CancellationToken token) =>
        await CheckAsync(tenantId, siteId, null, false, true, token);
    public async Task EnsureCanManageAsync(Guid tenantId, Guid siteId, CancellationToken token) =>
        await CheckAsync(tenantId, siteId, null, false, false, token);
    public async Task<Guid> EnsureCanReassignAsync(Guid tenantId, Guid siteId, Guid repairId, CancellationToken token) =>
        (await CheckAsync(tenantId, siteId, repairId, false, false, token)).ActorId;
    public async Task<Guid> EnsureCanTestAsync(Guid tenantId, Guid siteId, Guid repairId, CancellationToken token) =>
        (await CheckAsync(tenantId, siteId, repairId, true, false, token)).ActorId;
    public async Task<Guid> EnsureCanReturnToWorkAsync(Guid tenantId, Guid siteId, Guid repairId, CancellationToken token) =>
        (await CheckAsync(tenantId, siteId, repairId, true, false, token)).ActorId;
    public Task<SitePermission> EnsureCanReadAsync(Guid tenantId, Guid siteId, Guid? repairId, CancellationToken token) =>
        CheckAsync(tenantId, siteId, repairId, true, true, token);

    private async Task<SitePermission> CheckAsync(Guid tenantId, Guid siteId, Guid? repairId,
        bool technicians, bool receptionists, CancellationToken token)
    {
        var principal = http.HttpContext?.User;
        var issuer = principal?.FindFirst("iss")?.Value;
        var subject = principal?.FindFirst("sub")?.Value;
        if (principal?.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(issuer) || string.IsNullOrEmpty(subject))
            throw new UnauthorizedAccessException();
        await using var db = await factory.CreateDbContextAsync(token);
        var actor = await db.StaffIdentities.AsNoTracking().SingleOrDefaultAsync(x => x.IsActive && x.Issuer == issuer && x.Subject == subject, token)
            ?? throw new UnauthorizedAccessException();
        var grants = await db.StaffGrants.AsNoTracking().Where(x => x.UserId == actor.Id && x.TenantId == tenantId &&
            (x.Role == StaffRole.TenantAdministrator || x.SiteId == siteId)).ToArrayAsync(token);
        var all = grants.Any(x => x.Role is StaffRole.TenantAdministrator or StaffRole.SiteManager || receptionists && x.Role == StaffRole.Receptionist);
        var ids = technicians ? grants.Where(x => x.Role == StaffRole.Technician).Select(x => x.TechnicianId!.Value).ToArray() : [];
        if (!all && ids.Length == 0) throw new UnauthorizedAccessException();
        if (!await db.Sites.AnyAsync(x => x.TenantId == tenantId && x.SiteId == siteId, token)) throw new UnauthorizedAccessException();
        if (repairId is not null)
        {
            var repair = await db.Repairs.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.SiteId == siteId && x.Id == repairId, token)
                ?? throw new RepairNotFoundException();
            if (!all && (repair.TechnicianId is null || !ids.Contains(repair.TechnicianId.Value))) throw new UnauthorizedAccessException();
        }
        return new(actor.Id, all, ids);
    }
}
