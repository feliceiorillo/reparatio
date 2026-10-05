using Microsoft.EntityFrameworkCore;
using Reparatio.Repairs.Application;
using Reparatio.Repairs.Domain;
using Reparatio.Repairs.Infrastructure;
using Xunit;

namespace Reparatio.Repairs.Infrastructure.Tests;

public sealed class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("REPARATIO_SQL_CONNECTION")))
            Skip = "Set REPARATIO_SQL_CONNECTION to run against a migrated SQL Server database.";
    }
}

public class SqlRepairStoreTests
{
    [SqlFact]
    public async Task Opening_persists_repair_load_timestamp_and_idempotent_receipt()
    {
        await using var data = await Data.CreateAsync(true);
        var command = data.OpenCommand();
        var handler = data.OpenHandler();
        var first = await handler.HandleAsync(command);
        Assert.Equal(first, await handler.HandleAsync(command));
        await using var db = data.Factory.CreateDbContext();
        var repair = await db.Repairs.SingleAsync(r => r.TenantId == data.Tenant);
        Assert.Equal(first.TechnicianId, repair.TechnicianId);
        Assert.Equal(1, repair.ArrivalSequence);
        var tech = await db.Technicians.SingleAsync(t => t.TenantId == data.Tenant);
        Assert.Equal(1, tech.ActiveRepairCount);
        Assert.NotNull(tech.LastAssignedAt);
        Assert.Equal(1, await db.Receipts.CountAsync(r => r.TenantId == data.Tenant));
    }

    [SqlFact]
    public async Task Stale_version_has_no_partial_effects()
    {
        await using var data = await Data.CreateAsync(true);
        var command = data.OpenCommand();
        var snapshot = await data.Store.ReadAsync(command, default);
        await data.OpenHandler().HandleAsync(data.OpenCommand());
        var repair = Repair.Open(command.RepairId, data.Tenant, data.Site, snapshot.Candidates);
        Assert.False(await data.Store.TryCommitAsync(command, snapshot.Version, repair, DateTimeOffset.UtcNow, default));
        await using var db = data.Factory.CreateDbContext();
        Assert.Equal(1, await db.Repairs.CountAsync(r => r.TenantId == data.Tenant));
        Assert.Equal(1, (await db.Technicians.SingleAsync(t => t.TenantId == data.Tenant)).ActiveRepairCount);
    }

    [SqlFact]
    public async Task Availability_assigns_persistent_queue_in_arrival_order()
    {
        await using var data = await Data.CreateAsync(false);
        var first = data.OpenCommand();
        var second = data.OpenCommand();
        await data.OpenHandler().HandleAsync(first);
        await data.OpenHandler().HandleAsync(second);
        var command = data.AvailabilityCommand();
        var handler = data.AvailabilityHandler();
        var result = await handler.HandleAsync(command);
        Assert.Equal(new[] { first.RepairId, second.RepairId }, result.Assignments.Select(a => a.RepairId));
        var replay = await handler.HandleAsync(command);
        Assert.Equal(result.Assignments, replay.Assignments);
        await using var db = data.Factory.CreateDbContext();
        Assert.Equal(0, await db.Repairs.CountAsync(r => r.TenantId == data.Tenant && r.Status == RepairStatus.WaitingForAssignment));
        Assert.Equal(2, (await db.Technicians.SingleAsync(t => t.TenantId == data.Tenant)).ActiveRepairCount);
    }

    [SqlFact]
    public async Task Concurrent_duplicate_openings_create_one_repair()
    {
        await using var data = await Data.CreateAsync(true);
        var command = data.OpenCommand();
        var barrier = new BarrierStore(data.Store);
        var handler = new OpenRepairHandler(new Access(), barrier, TimeProvider.System);
        var results = await Task.WhenAll(handler.HandleAsync(command), handler.HandleAsync(command));
        Assert.Equal(results[0], results[1]);
        await using var db = data.Factory.CreateDbContext();
        Assert.Equal(1, await db.Repairs.CountAsync(r => r.TenantId == data.Tenant));
        Assert.Equal(1, (await db.Technicians.SingleAsync(t => t.TenantId == data.Tenant)).ActiveRepairCount);
    }

    [SqlFact]
    public async Task Concurrent_opening_and_availability_share_the_same_version_and_preserve_FIFO()
    {
        await using var data = await Data.CreateAsync(false);
        var first = data.OpenCommand();
        await data.OpenHandler().HandleAsync(first);
        var barrier = new BarrierStore(data.Store);
        var open = new OpenRepairHandler(new Access(), barrier, TimeProvider.System);
        var available = new SetTechnicianAvailabilityHandler(new Access(), barrier, TimeProvider.System);
        await Task.WhenAll(open.HandleAsync(data.OpenCommand()), available.HandleAsync(data.AvailabilityCommand()));
        await using var db = data.Factory.CreateDbContext();
        var repairs = await db.Repairs.Where(r => r.TenantId == data.Tenant).OrderBy(r => r.ArrivalSequence).ToListAsync();
        Assert.Equal(first.RepairId, repairs[0].Id);
        Assert.Equal(new long[] { 1, 2 }, repairs.Select(r => r.ArrivalSequence));
        Assert.All(repairs, r => Assert.Equal(RepairStatus.AwaitingDiagnosis, r.Status));
        Assert.Equal(2, (await db.Technicians.SingleAsync(t => t.TenantId == data.Tenant)).ActiveRepairCount);
        Assert.Equal(3, (await db.Sites.SingleAsync(s => s.TenantId == data.Tenant)).Version);
    }

    [SqlFact]
    public async Task Tenant_and_site_are_enforced_by_composite_foreign_keys()
    {
        await using var data = await Data.CreateAsync(true);
        await using var foreign = await Data.CreateAsync(true);
        await using var db = data.Factory.CreateDbContext();
        db.Repairs.Add(new() { TenantId = data.Tenant, SiteId = data.Site, Id = Guid.NewGuid(),
            TechnicianId = foreign.Technician, Status = RepairStatus.AwaitingDiagnosis, ArrivalSequence = 1, OpenedAt = DateTimeOffset.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Null((await data.Store.ReadAsync(data.OpenCommand(), default)).Receipt);
    }

    [SqlFact]
    public async Task Reused_payload_and_duplicate_repair_id_do_not_increment_load()
    {
        await using var data = await Data.CreateAsync(true);
        var command = data.OpenCommand();
        var handler = data.OpenHandler();
        await handler.HandleAsync(command);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(command with { RepairId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(command with { RequestId = Guid.NewGuid() }));
        await using var db = data.Factory.CreateDbContext();
        Assert.Equal(1, (await db.Technicians.SingleAsync(t => t.TenantId == data.Tenant)).ActiveRepairCount);
        Assert.Equal(1, (await db.Sites.SingleAsync(s => s.TenantId == data.Tenant)).Version);
    }

    [SqlFact]
    public async Task Reads_are_isolated_from_other_tenants_and_sites()
    {
        await using var data = await Data.CreateAsync(true);
        await using var foreign = await Data.CreateAsync(true);
        var otherSite = Guid.NewGuid();
        var otherTechnician = Guid.NewGuid();
        await using (var db = data.Factory.CreateDbContext())
        {
            db.Sites.Add(new() { TenantId = data.Tenant, SiteId = otherSite });
            db.Technicians.Add(new() { TenantId = data.Tenant, SiteId = otherSite, Id = otherTechnician, IsAvailable = true });
            await db.SaveChangesAsync();
        }
        var snapshot = await data.Store.ReadAsync(data.OpenCommand(), default);
        Assert.Equal(data.Technician, Assert.Single(snapshot.Candidates).Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => data.Store.ReadAsync(
            data.OpenCommand() with { TenantId = foreign.Tenant }, default));
        await using var invalid = data.Factory.CreateDbContext();
        invalid.Repairs.Add(new() { TenantId = data.Tenant, SiteId = data.Site, Id = Guid.NewGuid(),
            TechnicianId = otherTechnician, Status = RepairStatus.AwaitingDiagnosis, ArrivalSequence = 1, OpenedAt = DateTimeOffset.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => invalid.SaveChangesAsync());
    }

    [SqlFact]
    public async Task Database_rejects_negative_workload()
    {
        await using var data = await Data.CreateAsync(true);
        await using (var db = data.Factory.CreateDbContext())
        {
            var technician = await db.Technicians.SingleAsync(t => t.TenantId == data.Tenant);
            technician.ActiveRepairCount = -1;
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using var read = data.Factory.CreateDbContext();
        Assert.Equal(0, (await read.Technicians.SingleAsync(t => t.TenantId == data.Tenant)).ActiveRepairCount);
    }
    private sealed class Access : IRepairAccess, ITechnicianAvailabilityAccess
    {
        public Task EnsureCanReceiveAsync(Guid tenantId, Guid siteId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task EnsureCanManageAsync(Guid tenantId, Guid siteId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class BarrierStore(SqlRepairStore inner) : IRepairOpeningStore, ITechnicianAvailabilityStore
    {
        private int reads;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private async Task Wait(CancellationToken token)
        {
            if (Interlocked.Increment(ref reads) >= 2) ready.TrySetResult();
            await ready.Task.WaitAsync(token);
        }
        public async Task<AssignmentSnapshot> ReadAsync(OpenRepairCommand c, CancellationToken token)
        { var result = await inner.ReadAsync(c, token); await Wait(token); return result; }
        public async Task<AvailabilitySnapshot> ReadAsync(SetTechnicianAvailabilityCommand c, CancellationToken token)
        { var result = await inner.ReadAsync(c, token); await Wait(token); return result; }
        public Task<bool> TryCommitAsync(OpenRepairCommand c, long v, Repair r, DateTimeOffset at, CancellationToken token)
            => inner.TryCommitAsync(c, v, r, at, token);
        public Task<bool> TryCommitAsync(SetTechnicianAvailabilityCommand c, long v, IReadOnlyList<WaitingAssignment> a, DateTimeOffset at, CancellationToken token)
            => inner.TryCommitAsync(c, v, a, at, token);
    }
    private sealed class Data : IAsyncDisposable
    {
        public Guid Tenant { get; } = Guid.NewGuid();
        public Guid Site { get; } = Guid.NewGuid();
        public Guid Technician { get; } = Guid.NewGuid();
        public RepairsDbContextFactory Factory { get; } = new(Environment.GetEnvironmentVariable("REPARATIO_SQL_CONNECTION")!);
        public SqlRepairStore Store => new(Factory);
        public OpenRepairCommand OpenCommand() => new(Guid.NewGuid(), Guid.NewGuid(), Tenant, Site);
        public SetTechnicianAvailabilityCommand AvailabilityCommand() => new(Guid.NewGuid(), Tenant, Site, Technician, true);
        public OpenRepairHandler OpenHandler() => new(new Access(), Store, TimeProvider.System);
        public SetTechnicianAvailabilityHandler AvailabilityHandler() => new(new Access(), Store, TimeProvider.System);
        public static async Task<Data> CreateAsync(bool available)
        {
            var data = new Data();
            await using var db = data.Factory.CreateDbContext();
            db.Sites.Add(new() { TenantId = data.Tenant, SiteId = data.Site });
            db.Technicians.Add(new() { TenantId = data.Tenant, SiteId = data.Site, Id = data.Technician, IsAvailable = available });
            await db.SaveChangesAsync();
            return data;
        }
        public async ValueTask DisposeAsync()
        {
            // Remove only this fixture's randomly generated tenant; never drop the database.
            await using var db = Factory.CreateDbContext();
            await db.Reassignments.Where(r => r.TenantId == Tenant).ExecuteDeleteAsync();
            await db.Receipts.Where(r => r.TenantId == Tenant).ExecuteDeleteAsync();
            await db.Repairs.Where(r => r.TenantId == Tenant).ExecuteDeleteAsync();
            await db.Technicians.Where(r => r.TenantId == Tenant).ExecuteDeleteAsync();
            await db.Sites.Where(r => r.TenantId == Tenant).ExecuteDeleteAsync();
        }
    }
}
