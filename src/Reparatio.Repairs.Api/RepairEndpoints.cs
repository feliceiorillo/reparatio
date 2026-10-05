using Microsoft.EntityFrameworkCore;
using Reparatio.Repairs.Application;
using Reparatio.Repairs.Infrastructure;

namespace Reparatio.Repairs.Api;

public sealed record OpenRepairRequest(Guid RequestId, Guid RepairId);
public sealed record AvailabilityRequest(Guid RequestId, bool IsAvailable);
public sealed record ReassignRequest(Guid RequestId, Guid TechnicianId, string Reason);
public sealed record SubmitTestingRequest(Guid RequestId);
public sealed record TestingRequest(Guid RequestId, bool? Passed, string? Notes = null);
public sealed record ReturnWorkRequest(Guid RequestId, string Reason);
public static class RepairEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/tenants/{tenantId:guid}/sites/{siteId:guid}").RequireAuthorization("RepairApi");
        group.MapPost("/repairs", async (Guid tenantId, Guid siteId, OpenRepairRequest body, OpenRepairHandler handler, CancellationToken token) =>
            Results.Ok(await handler.HandleAsync(new(body.RequestId, body.RepairId, tenantId, siteId), token)));
        group.MapPost("/technicians/{technicianId:guid}/availability", async (Guid tenantId, Guid siteId, Guid technicianId, AvailabilityRequest body,
            SetTechnicianAvailabilityHandler handler, CancellationToken token) => Results.Ok(await handler.HandleAsync(new(body.RequestId, tenantId, siteId, technicianId, body.IsAvailable), token)));
        group.MapPost("/repairs/{repairId:guid}/reassign", async (Guid tenantId, Guid siteId, Guid repairId, ReassignRequest body, RepairLifecycleHandler handler, CancellationToken token) =>
            Results.Ok(await handler.HandleAsync(new ReassignRepairCommand(body.RequestId, tenantId, siteId, repairId, body.TechnicianId, body.Reason), token)));
        group.MapPost("/repairs/{repairId:guid}/submit-testing", async (Guid tenantId, Guid siteId, Guid repairId, SubmitTestingRequest body, RepairLifecycleHandler handler, CancellationToken token) =>
            Results.Ok(await handler.HandleAsync(new SubmitRepairForTestingCommand(body.RequestId, tenantId, siteId, repairId), token)));
        group.MapPost("/repairs/{repairId:guid}/testing", async (Guid tenantId, Guid siteId, Guid repairId, TestingRequest body, RepairLifecycleHandler handler, CancellationToken token) =>
            Results.Ok(await handler.HandleAsync(new RecordRepairTestingCommand(body.RequestId, tenantId, siteId, repairId, body.Passed ?? throw new ArgumentException("Testing outcome is required."), body.Notes), token)));
        group.MapPost("/repairs/{repairId:guid}/return-work", async (Guid tenantId, Guid siteId, Guid repairId, ReturnWorkRequest body, RepairLifecycleHandler handler, CancellationToken token) =>
            Results.Ok(await handler.HandleAsync(new ReturnRepairToWorkCommand(body.RequestId, tenantId, siteId, repairId, body.Reason), token)));
        group.MapGet("/repairs/{repairId:guid}", async (Guid tenantId, Guid siteId, Guid repairId, StaffAccess access, IDbContextFactory<RepairsDbContext> factory, CancellationToken token) => {
            await access.EnsureCanReadAsync(tenantId, siteId, repairId, token);
            await using var db = await factory.CreateDbContextAsync(token);
            var row = await db.Repairs.AsNoTracking().SingleOrDefaultAsync(r => r.TenantId == tenantId && r.SiteId == siteId && r.Id == repairId, token);
            return row is null ? Results.NotFound() : Results.Ok(new { repairId = row.Id, row.TechnicianId, row.Status });
        });
        group.MapGet("/repairs", async (Guid tenantId, Guid siteId, StaffAccess access, IDbContextFactory<RepairsDbContext> factory, CancellationToken token) => {
            var permission = await access.EnsureCanReadAsync(tenantId, siteId, null, token);
            await using var db = await factory.CreateDbContextAsync(token);
            var query = db.Repairs.AsNoTracking().Where(r => r.TenantId == tenantId && r.SiteId == siteId);
            if (!permission.AllRepairs) query = query.Where(r => r.TechnicianId != null && permission.TechnicianIds.Contains(r.TechnicianId.Value));
            return Results.Ok(await query.OrderBy(r => r.ArrivalSequence).Take(100).Select(r => new { repairId = r.Id, r.TechnicianId, r.Status }).ToArrayAsync(token));
        });
    }
}
