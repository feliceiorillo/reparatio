using Reparatio.Repairs.Application;
using Reparatio.Repairs.Domain;
using Xunit;

namespace Reparatio.Repairs.Application.Tests;

public class RepairLifecycleCommandTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-05T12:00:00Z");
    [Fact]
    public async Task Reassignment_uses_authenticated_actor_and_server_time()
    {
        var store = new Store();
        var actor = Guid.NewGuid();
        var result = await Handler(store, actor).HandleAsync(store.Reassign());
        Assert.Equal(store.Target.Id, result.TechnicianId);
        var audit = Assert.Single(store.State.Reassignments);
        Assert.Equal(actor, audit.ActorId);
        Assert.Equal(At, audit.OccurredAt);
        Assert.Equal("Assenza", audit.Reason);
    }
    [Fact]
    public async Task Access_is_checked_before_read_and_again_on_replay()
    {
        var store = new Store();
        var command = store.Reassign();
        await Handler(store).HandleAsync(command);
        var reads = store.Reads;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Handler(store, allow: false).HandleAsync(command));
        Assert.Equal(reads, store.Reads);
    }
    [Fact]
    public async Task Identical_request_replays_original_result_and_changed_payload_fails()
    {
        var store = new Store();
        var command = store.Reassign();
        var handler = Handler(store);
        var first = await handler.HandleAsync(command);
        Assert.Equal(first, await handler.HandleAsync(command));
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(command with { Reason = "Altro motivo" }));
        Assert.Single(store.State.Reassignments);
    }
    [Fact]
    public async Task Retry_reloads_state_and_persistent_conflicts_are_bounded()
    {
        var store = new Store { Conflicts = 1 };
        await Handler(store).HandleAsync(store.Reassign());
        Assert.Equal(2, store.Attempts);
        store = new Store { Conflicts = 100 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(store).HandleAsync(store.Reassign()));
        Assert.Equal(5, store.Attempts);
        Assert.Empty(store.State.Reassignments);
    }
    [Fact]
    public async Task Testing_and_return_to_work_follow_domain_transitions()
    {
        var store = new Store();
        var handler = Handler(store);
        await handler.HandleAsync(store.Submit());
        await handler.HandleAsync(store.Test(false));
        Assert.Equal(RepairStatus.InProgress, store.State.Status);
        await handler.HandleAsync(store.Submit());
        await handler.HandleAsync(store.Test(true));
        Assert.Equal(RepairStatus.ReadyForCollection, store.State.Status);
        await handler.HandleAsync(store.Return());
        Assert.Equal(RepairStatus.InProgress, store.State.Status);
    }
    [Fact]
    public async Task Invalid_transition_or_target_does_not_commit()
    {
        var store = new Store();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(store).HandleAsync(store.Test(true)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(store).HandleAsync(store.Reassign() with { TechnicianId = Guid.NewGuid() }));
        Assert.Equal(0, store.Attempts);
        Assert.Equal(RepairStatus.InProgress, store.State.Status);
    }
    [Fact]
    public async Task Invalid_input_and_cancellation_prevent_storage_access()
    {
        var store = new Store();
        await Assert.ThrowsAsync<ArgumentException>(() => Handler(store).HandleAsync(store.Reassign() with { Reason = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => Handler(store).HandleAsync(store.Test(true) with { Notes = new string('x', 2001) }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Handler(store).HandleAsync(store.Reassign(), cancellation.Token));
        Assert.Equal(0, store.Reads);
    }
    private static RepairLifecycleHandler Handler(Store store, Guid? actor = null, bool allow = true)
        => new(new Access(actor ?? Guid.NewGuid(), allow), store, new Clock());
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => At; }
    private sealed class Access(Guid actor, bool allow) : IRepairLifecycleAccess
    {
        private Task<Guid> Check() => allow ? Task.FromResult(actor) : throw new UnauthorizedAccessException();
        public Task<Guid> EnsureCanReassignAsync(Guid tenantId, Guid siteId, Guid repairId, CancellationToken token) => Check();
        public Task<Guid> EnsureCanTestAsync(Guid tenantId, Guid siteId, Guid repairId, CancellationToken token) => Check();
        public Task<Guid> EnsureCanReturnToWorkAsync(Guid tenantId, Guid siteId, Guid repairId, CancellationToken token) => Check();
    }
    private sealed class Store : IRepairLifecycleStore
    {
        public RepairState State { get; private set; } = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), RepairStatus.InProgress, []);
        public TechnicianCandidate Target => new(Guid.Parse("00000000-0000-0000-0000-000000000001"), State.TenantId, State.SiteId, true, 0, null);
        private readonly List<RepairLifecycleReceipt> receipts = [];
        private long version;
        public int Conflicts { get; init; }
        private int conflictsUsed;
        public int Attempts { get; private set; }
        public int Reads { get; private set; }
        public ReassignRepairCommand Reassign() => new(Guid.NewGuid(), State.TenantId, State.SiteId, State.Id, Target.Id, "Assenza");
        public SubmitRepairForTestingCommand Submit() => new(Guid.NewGuid(), State.TenantId, State.SiteId, State.Id);
        public RecordRepairTestingCommand Test(bool passed) => new(Guid.NewGuid(), State.TenantId, State.SiteId, State.Id, passed);
        public ReturnRepairToWorkCommand Return() => new(Guid.NewGuid(), State.TenantId, State.SiteId, State.Id, "Verifica aggiuntiva");
        public Task<RepairLifecycleSnapshot> ReadAsync(RepairLifecycleCommand command, CancellationToken token)
        {
            Reads++;
            return Task.FromResult(new RepairLifecycleSnapshot(version, State, [Target], receipts.SingleOrDefault(r =>
                r.Command.Operation == command.Operation && r.Command.RequestId == command.RequestId)));
        }
        public Task<bool> TryCommitAsync(RepairLifecycleCommand command, long expectedVersion, RepairState updated,
            Guid actorId, DateTimeOffset occurredAt, CancellationToken token)
        {
            Attempts++;
            if (conflictsUsed++ < Conflicts || version != expectedVersion) return Task.FromResult(false);
            State = updated;
            receipts.Add(new(command, new(updated.Id, updated.TechnicianId, updated.Status)));
            version++;
            return Task.FromResult(true);
        }
    }
}
