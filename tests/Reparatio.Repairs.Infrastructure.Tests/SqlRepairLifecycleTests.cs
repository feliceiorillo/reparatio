using Microsoft.EntityFrameworkCore;
using Reparatio.Repairs.Application;
using Reparatio.Repairs.Domain;
using Reparatio.Repairs.Infrastructure;
using Xunit;

namespace Reparatio.Repairs.Infrastructure.Tests;

public class SqlRepairLifecycleTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-05T14:00:00Z");
    [SqlFact]
    public async Task Reassignment_persists_history_and_moves_load_even_if_previous_technician_is_absent()
    {
        await using var data = await Data.Create(RepairStatus.InProgress, oldAvailable: false);
        var command = data.Reassign();
        var handler = data.Handler();
        var result = await handler.HandleAsync(command);
        Assert.Equal(result, await handler.HandleAsync(command));
        await using var db = data.Factory.CreateDbContext();
        var row = await db.Repairs.SingleAsync(r => r.TenantId == data.Tenant);
        Assert.Equal(data.Target, row.TechnicianId);
        Assert.Equal(RepairStatus.InProgress, row.Status);
        Assert.Equal(0, (await db.Technicians.SingleAsync(t => t.TenantId == data.Tenant && t.Id == data.Old)).ActiveRepairCount);
        var target = await db.Technicians.SingleAsync(t => t.TenantId == data.Tenant && t.Id == data.Target);
        Assert.Equal(1, target.ActiveRepairCount);
        Assert.Equal(At, target.LastAssignedAt);
        var audit = await db.Reassignments.SingleAsync(r => r.TenantId == data.Tenant);
        Assert.Equal(data.Old, audit.PreviousTechnicianId);
        Assert.Equal(data.Target, audit.NewTechnicianId);
        Assert.Equal(data.Actor, audit.ActorId);
        Assert.Equal(At, audit.OccurredAt);
        Assert.Equal("Assenza", audit.Reason);
    }
    [SqlFact]
    public async Task Second_reassignment_rehydrates_history_without_duplicating_previous_entries()
    {
        await using var data = await Data.Create(RepairStatus.InProgress);
        await data.Handler().HandleAsync(data.Reassign());
        await data.Handler().HandleAsync(data.Reassign() with { TechnicianId = data.Old, Reason = "Rientro" });
        var snapshot = await data.Store.ReadAsync(data.Reassign(), default);
        Assert.Equal(2, snapshot.Repair!.Reassignments.Count);
        Assert.Equal(data.Old, snapshot.Repair.TechnicianId);
        await using var db = data.Factory.CreateDbContext();
        Assert.Equal(2, await db.Reassignments.CountAsync(r => r.TenantId == data.Tenant));
        Assert.Equal(1, (await db.Technicians.SingleAsync(t => t.TenantId == data.Tenant && t.Id == data.Old)).ActiveRepairCount);
        Assert.Equal(0, (await db.Technicians.SingleAsync(t => t.TenantId == data.Tenant && t.Id == data.Target)).ActiveRepairCount);
    }
    [SqlFact]
    public async Task Testing_submission_and_positive_result_persist_audit_and_remove_workload_once()
    {
        await using var data = await Data.Create(RepairStatus.InProgress);
        var handler = data.Handler();
        await handler.HandleAsync(data.Submit());
        var command = data.Test(true) with { Notes = "Touchscreen e ricarica verificati" };
        var result = await handler.HandleAsync(command);
        Assert.Equal(result, await handler.HandleAsync(command));
        await using var db = data.Factory.CreateDbContext();
        Assert.Equal(RepairStatus.ReadyForCollection, (await db.Repairs.SingleAsync(r => r.TenantId == data.Tenant)).Status);
        var technician = await db.Technicians.SingleAsync(t => t.TenantId == data.Tenant && t.Id == data.Old);
        Assert.Equal(0, technician.ActiveRepairCount);
        Assert.Equal(At.AddDays(-1), technician.LastAssignedAt);
        var events = await db.RepairTransitions.Where(t => t.TenantId == data.Tenant).OrderBy(t => t.Id).ToListAsync();
        Assert.Equal(2, events.Count);
        Assert.Equal(RepairStatus.InProgress, events[0].PreviousStatus);
        Assert.Equal(RepairStatus.AwaitingTesting, events[0].NewStatus);
        Assert.True(events[1].Passed);
        Assert.Equal(command.Notes, events[1].Notes);
        Assert.All(events, e => { Assert.Equal(data.Actor, e.ActorId); Assert.Equal(At, e.OccurredAt); });
    }
    [SqlFact]
    public async Task Failed_testing_returns_to_work_without_changing_load_or_rotation()
    {
        await using var data = await Data.Create(RepairStatus.AwaitingTesting);
        await data.Handler().HandleAsync(data.Test(false));
        await using var db = data.Factory.CreateDbContext();
        Assert.Equal(RepairStatus.InProgress, (await db.Repairs.SingleAsync(r => r.TenantId == data.Tenant)).Status);
        var tech = await db.Technicians.SingleAsync(t => t.TenantId == data.Tenant && t.Id == data.Old);
        Assert.Equal(1, tech.ActiveRepairCount);
        Assert.Equal(At.AddDays(-1), tech.LastAssignedAt);
        Assert.False((await db.RepairTransitions.SingleAsync(t => t.TenantId == data.Tenant)).Passed);
    }
    [SqlFact]
    public async Task Reopening_reinstates_workload_once_and_replay_preserves_original_testing_result()
    {
        await using var data = await Data.Create(RepairStatus.AwaitingTesting);
        var handler = data.Handler();
        var testing = data.Test(true);
        var original = await handler.HandleAsync(testing);
        var reopening = data.Return();
        await handler.HandleAsync(reopening);
        await handler.HandleAsync(reopening);
        Assert.Equal(original, await handler.HandleAsync(testing));
        await using var db = data.Factory.CreateDbContext();
        Assert.Equal(RepairStatus.InProgress, (await db.Repairs.SingleAsync(r => r.TenantId == data.Tenant)).Status);
        var tech = await db.Technicians.SingleAsync(t => t.TenantId == data.Tenant && t.Id == data.Old);
        Assert.Equal(1, tech.ActiveRepairCount);
        Assert.Equal(At.AddDays(-1), tech.LastAssignedAt);
        Assert.Equal(2, await db.RepairTransitions.CountAsync(t => t.TenantId == data.Tenant));
    }
    [SqlFact]
    public async Task Invalid_targets_states_and_scopes_do_not_change_any_persistent_data()
    {
        await using var data = await Data.Create(RepairStatus.InProgress, targetAvailable: false);
        await using var foreign = await Data.Create(RepairStatus.InProgress);
        var handler = data.Handler();
        foreach (var technician in new[] { data.Target, data.Old, foreign.Target, Guid.NewGuid() })
            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(data.Reassign() with { TechnicianId = technician }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(data.Test(true)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(data.Reassign() with { SiteId = foreign.Site }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(data.Reassign() with { RepairId = foreign.Repair }));
        await using var db = data.Factory.CreateDbContext();
        Assert.Equal(0, (await db.Sites.SingleAsync(s => s.TenantId == data.Tenant)).Version);
        Assert.Equal(data.Old, (await db.Repairs.SingleAsync(r => r.TenantId == data.Tenant)).TechnicianId);
        Assert.Equal(0, await db.Reassignments.CountAsync(r => r.TenantId == data.Tenant));
        Assert.Equal(0, await db.Receipts.CountAsync(r => r.TenantId == data.Tenant));
    }
    [SqlFact]
    public async Task Stale_version_does_not_write_audit_or_move_load()
    {
        await using var data = await Data.Create(RepairStatus.InProgress);
        var command = data.Reassign();
        var snapshot = await data.Store.ReadAsync(command, default);
        var repair = Domain.Repair.Restore(snapshot.Repair!);
        RepairLifecycleBehavior.Apply(repair, command, snapshot.Candidates, data.Actor, At);
        await data.Handler().HandleAsync(data.Submit());
        Assert.False(await data.Store.TryCommitAsync(command, snapshot.Version, repair.Snapshot(), data.Actor, At, default));
        await using var db = data.Factory.CreateDbContext();
        Assert.Equal(0, await db.Reassignments.CountAsync(r => r.TenantId == data.Tenant));
        Assert.Equal(data.Old, (await db.Repairs.SingleAsync(r => r.TenantId == data.Tenant)).TechnicianId);
    }
    [SqlFact]
    public async Task Inconsistent_workload_rolls_back_repair_history_and_site_version()
    {
        await using var data = await Data.Create(RepairStatus.InProgress);
        await using (var seed = data.Factory.CreateDbContext())
            await seed.Technicians.Where(t => t.TenantId == data.Tenant && t.Id == data.Old).ExecuteUpdateAsync(u => u.SetProperty(t => t.ActiveRepairCount, 0));
        await Assert.ThrowsAsync<InvalidOperationException>(() => data.Handler().HandleAsync(data.Reassign()));
        await using var db = data.Factory.CreateDbContext();
        Assert.Equal(0, (await db.Sites.SingleAsync(s => s.TenantId == data.Tenant)).Version);
        Assert.Equal(data.Old, (await db.Repairs.SingleAsync(r => r.TenantId == data.Tenant)).TechnicianId);
        Assert.Equal(0, await db.Reassignments.CountAsync(r => r.TenantId == data.Tenant));
        Assert.Equal(0, await db.Receipts.CountAsync(r => r.TenantId == data.Tenant));
        Assert.All(await db.Technicians.Where(t => t.TenantId == data.Tenant).ToListAsync(), t => Assert.Equal(0, t.ActiveRepairCount));
    }
    [SqlFact]
    public async Task Concurrent_duplicate_reassignments_move_load_and_append_history_only_once()
    {
        await using var data = await Data.Create(RepairStatus.InProgress);
        var store = new BarrierStore(data.Store);
        var handler = data.Handler(store);
        var command = data.Reassign();
        var results = await Task.WhenAll(handler.HandleAsync(command), handler.HandleAsync(command));
        Assert.Equal(results[0], results[1]);
        await using var db = data.Factory.CreateDbContext();
        Assert.Equal(1, await db.Reassignments.CountAsync(r => r.TenantId == data.Tenant));
        Assert.Equal(1, await db.Receipts.CountAsync(r => r.TenantId == data.Tenant));
        Assert.Equal(1, (await db.Technicians.SingleAsync(t => t.TenantId == data.Tenant && t.Id == data.Target)).ActiveRepairCount);
    }
    [SqlFact]
    public async Task Concurrent_testing_and_reassignment_preserve_load_and_valid_final_state()
    {
        await using var data = await Data.Create(RepairStatus.AwaitingTesting);
        var store = new BarrierStore(data.Store);
        var handler = data.Handler(store);
        var testing = handler.HandleAsync(data.Test(true));
        var reassignment = CaptureReassignment();
        await Task.WhenAll(testing, reassignment);
        await using var db = data.Factory.CreateDbContext();
        Assert.Equal(RepairStatus.ReadyForCollection, (await db.Repairs.SingleAsync(r => r.TenantId == data.Tenant)).Status);
        Assert.All(await db.Technicians.Where(t => t.TenantId == data.Tenant).ToListAsync(), t => Assert.Equal(0, t.ActiveRepairCount));
        Assert.Equal(1, await db.RepairTransitions.CountAsync(t => t.TenantId == data.Tenant));
        var count = await db.Reassignments.CountAsync(r => r.TenantId == data.Tenant);
        Assert.InRange(count, 0, 1);
        Assert.Equal(1 + count, await db.Receipts.CountAsync(r => r.TenantId == data.Tenant));
        async Task CaptureReassignment()
        {
            try { await handler.HandleAsync(data.Reassign()); }
            catch (InvalidOperationException e) when (e.Message == "Only assigned repairs with pending work can be reassigned.")
            { /* Positive testing can win first, closing pending work. */ }
        }
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => At; }
    private sealed class Access(Guid actor) : IRepairLifecycleAccess
    {
        public Task<Guid> EnsureCanReassignAsync(Guid tenantId, Guid siteId, Guid repairId, CancellationToken token) => Task.FromResult(actor);
        public Task<Guid> EnsureCanTestAsync(Guid tenantId, Guid siteId, Guid repairId, CancellationToken token) => Task.FromResult(actor);
        public Task<Guid> EnsureCanReturnToWorkAsync(Guid tenantId, Guid siteId, Guid repairId, CancellationToken token) => Task.FromResult(actor);
    }
    private sealed class BarrierStore(IRepairLifecycleStore inner) : IRepairLifecycleStore
    {
        private int reads;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<RepairLifecycleSnapshot> ReadAsync(RepairLifecycleCommand c, CancellationToken token)
        {
            var snapshot = await inner.ReadAsync(c, token);
            if (Interlocked.Increment(ref reads) >= 2) ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
            return snapshot;
        }
        public Task<bool> TryCommitAsync(RepairLifecycleCommand c, long version, RepairState updated,
            Guid actor, DateTimeOffset at, CancellationToken token) => inner.TryCommitAsync(c, version, updated, actor, at, token);
    }
    private sealed class Data : IAsyncDisposable
    {
        public Guid Tenant { get; } = Guid.NewGuid();
        public Guid Site { get; } = Guid.NewGuid();
        public Guid Repair { get; } = Guid.NewGuid();
        public Guid Old { get; } = Guid.NewGuid();
        public Guid Target { get; } = Guid.NewGuid();
        public Guid Actor { get; } = Guid.NewGuid();
        public RepairsDbContextFactory Factory { get; } = new(SqlConnectionSettings.ReadRequired());
        public SqlRepairStore Store => new(Factory);
        public RepairLifecycleHandler Handler(IRepairLifecycleStore? store = null) => new(new Access(Actor), store ?? Store, new Clock());
        public ReassignRepairCommand Reassign() => new(Guid.NewGuid(), Tenant, Site, Repair, Target, "Assenza");
        public SubmitRepairForTestingCommand Submit() => new(Guid.NewGuid(), Tenant, Site, Repair);
        public RecordRepairTestingCommand Test(bool passed) => new(Guid.NewGuid(), Tenant, Site, Repair, passed);
        public ReturnRepairToWorkCommand Return() => new(Guid.NewGuid(), Tenant, Site, Repair, "Verifica aggiuntiva");
        public static async Task<Data> Create(RepairStatus status, bool oldAvailable = true, bool targetAvailable = true)
        {
            var data = new Data();
            await using var db = data.Factory.CreateDbContext();
            db.Sites.Add(new() { TenantId = data.Tenant, SiteId = data.Site, NextArrivalSequence = 2 });
            db.Technicians.Add(new() { TenantId = data.Tenant, SiteId = data.Site, Id = data.Old,
                IsAvailable = oldAvailable, ActiveRepairCount = RepairWorkload.Counts(status) ? 1 : 0, LastAssignedAt = At.AddDays(-1) });
            db.Technicians.Add(new() { TenantId = data.Tenant, SiteId = data.Site, Id = data.Target, IsAvailable = targetAvailable });
            db.Repairs.Add(new() { TenantId = data.Tenant, SiteId = data.Site, Id = data.Repair,
                TechnicianId = data.Old, Status = status, ArrivalSequence = 1, OpenedAt = At.AddDays(-1) });
            await db.SaveChangesAsync();
            return data;
        }
        public async ValueTask DisposeAsync()
        {
            await using var db = Factory.CreateDbContext();
            await db.RepairTransitions.Where(r => r.TenantId == Tenant).ExecuteDeleteAsync();
            await db.Reassignments.Where(r => r.TenantId == Tenant).ExecuteDeleteAsync();
            await db.Receipts.Where(r => r.TenantId == Tenant).ExecuteDeleteAsync();
            await db.Repairs.Where(r => r.TenantId == Tenant).ExecuteDeleteAsync();
            await db.Technicians.Where(r => r.TenantId == Tenant).ExecuteDeleteAsync();
            await db.Sites.Where(r => r.TenantId == Tenant).ExecuteDeleteAsync();
        }
    }
}
