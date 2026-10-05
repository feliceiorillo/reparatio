using Reparatio.Repairs.Application;
using Reparatio.Repairs.Domain;
using Xunit;

namespace Reparatio.Repairs.Application.Tests;

public class TechnicianAvailabilityTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Site = Guid.NewGuid();
    private static SetTechnicianAvailabilityCommand Command(Store store, bool available = true)
        => new(Guid.NewGuid(), Tenant, Site, store.Technician.Id, available);
    private static SetTechnicianAvailabilityHandler Handler(Store store, bool allow = true)
        => new(new Access(allow), store, TimeProvider.System);

    [Fact]
    public async Task Returning_technician_assigns_entire_queue_in_FIFO_order_and_updates_workload()
    {
        var store = new Store();
        var result = await Handler(store).HandleAsync(Command(store));
        Assert.Equal(store.ArrivalOrder, result.Assignments.Select(a => a.RepairId));
        Assert.Equal(2, store.Technician.ActiveRepairCount);
        Assert.True(store.Technician.IsAvailable);
        Assert.NotNull(store.Technician.LastAssignedAt);
        Assert.Empty(store.Waiting);
        Assert.All(store.Repairs.Values, r => Assert.Equal(RepairStatus.AwaitingDiagnosis, r.Status));
    }

    [Fact]
    public async Task Replayed_request_does_not_assign_twice_and_changed_payload_is_rejected()
    {
        var store = new Store();
        var command = Command(store);
        var handler = Handler(store);
        var first = await handler.HandleAsync(command);
        Assert.Equal(first.Assignments, (await handler.HandleAsync(command)).Assignments);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(command with { IsAvailable = false }));
        Assert.Equal(2, store.Technician.ActiveRepairCount);
        Assert.Single(store.Receipts);
    }

    [Fact]
    public async Task Unavailability_does_not_assign_or_remove_existing_workload()
    {
        var store = new Store();
        var result = await Handler(store).HandleAsync(Command(store, false));
        Assert.Empty(result.Assignments);
        Assert.False(store.Technician.IsAvailable);
        Assert.Equal(2, store.Waiting.Count);
        Assert.Equal(0, store.Technician.ActiveRepairCount);
    }

    [Fact]
    public async Task Conflicts_retry_and_persistent_conflicts_leave_everything_unchanged()
    {
        var store = new Store { Conflicts = 1 };
        await Handler(store).HandleAsync(Command(store));
        Assert.Equal(2, store.Attempts);
        store = new Store { Conflicts = 100 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(store).HandleAsync(Command(store)));
        Assert.Equal(5, store.Attempts);
        Assert.False(store.Technician.IsAvailable);
        Assert.Equal(2, store.Waiting.Count);
        Assert.Empty(store.Receipts);
    }

    [Fact]
    public async Task Missing_or_foreign_technician_is_rejected_without_write()
    {
        var store = new Store();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(store).HandleAsync(Command(store) with { SiteId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(store).HandleAsync(Command(store) with { TechnicianId = Guid.NewGuid() }));
        Assert.Equal(0, store.Attempts);
        Assert.False(store.Technician.IsAvailable);
    }

    [Fact]
    public async Task Access_is_checked_again_on_replay_and_cancellation_prevents_reads()
    {
        var store = new Store();
        var command = Command(store);
        await Handler(store).HandleAsync(command);
        var reads = store.Reads;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Handler(store, false).HandleAsync(command));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Handler(store).HandleAsync(command, cancellation.Token));
        Assert.Equal(reads, store.Reads);
    }

    [Fact]
    public async Task Concurrent_availability_changes_do_not_assign_same_queue_twice()
    {
        var store = new Store { InitialReaders = 2 };
        var handler = Handler(store);
        await Task.WhenAll(handler.HandleAsync(Command(store)), handler.HandleAsync(Command(store)));
        Assert.Equal(2, store.Technician.ActiveRepairCount);
        Assert.Equal(2, store.Receipts.Count);
        Assert.Equal(2, store.Receipts.Sum(r => r.Result.Assignments.Count));
        Assert.True(store.Attempts > 2);
    }

    private sealed class Access(bool allow) : ITechnicianAvailabilityAccess
    {
        public Task EnsureCanManageAsync(Guid tenantId, Guid siteId, CancellationToken cancellationToken)
        {
            if (!allow) throw new UnauthorizedAccessException();
            return Task.CompletedTask;
        }
    }

    // In-memory adapter only for transaction contract tests.
    private sealed class Store : ITechnicianAvailabilityStore
    {
        private readonly object gate = new();
        private readonly TaskCompletionSource firstReads = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private long version;
        public int InitialReaders { get; init; }
        public int Reads { get; private set; }
        public int Attempts { get; private set; }
        public int Conflicts { get; set; }
        public TechnicianCandidate Technician { get; private set; } = new(Guid.NewGuid(), Tenant, Site, false, 0, null);
        public List<WaitingRepairCandidate> Waiting { get; } = [];
        public Dictionary<Guid, Repair> Repairs { get; } = [];
        public List<Guid> ArrivalOrder { get; } = [];
        public List<AvailabilityReceipt> Receipts { get; } = [];

        public Store()
        {
            for (var sequence = 1; sequence <= 2; sequence++)
            {
                var repair = Repair.Open(Guid.NewGuid(), Tenant, Site, []);
                ArrivalOrder.Add(repair.Id);
                Repairs.Add(repair.Id, repair);
                Waiting.Insert(0, new(repair.Id, Tenant, Site, sequence));
            }
        }

        public async Task<AvailabilitySnapshot> ReadAsync(SetTechnicianAvailabilityCommand command, CancellationToken cancellationToken)
        {
            AvailabilitySnapshot snapshot;
            lock (gate)
            {
                Reads++;
                snapshot = new(version, [Technician], Waiting.ToArray(), Receipts.SingleOrDefault(r =>
                    r.Command.TenantId == command.TenantId && r.Command.RequestId == command.RequestId));
                if (Reads >= InitialReaders) firstReads.TrySetResult();
            }
            if (InitialReaders > 0) await firstReads.Task.WaitAsync(cancellationToken);
            return snapshot;
        }

        public Task<bool> TryCommitAsync(SetTechnicianAvailabilityCommand command, long expectedVersion,
            IReadOnlyList<WaitingAssignment> assignments, DateTimeOffset assignedAt, CancellationToken cancellationToken)
        {
            lock (gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Attempts++;
                if (Conflicts > 0) { Conflicts--; return Task.FromResult(false); }
                if (version != expectedVersion) return Task.FromResult(false);
                Technician = new(Technician.Id, Tenant, Site, command.IsAvailable, Technician.ActiveRepairCount, Technician.LastAssignedAt);
                foreach (var assignment in assignments)
                {
                    Repairs[assignment.RepairId].AssignWaiting(Technician);
                    Waiting.RemoveAll(r => r.RepairId == assignment.RepairId);
                    Technician = new(Technician.Id, Tenant, Site, command.IsAvailable, Technician.ActiveRepairCount + 1, assignedAt);
                }
                Receipts.Add(new(command, new(Array.AsReadOnly(assignments.ToArray()))));
                version++;
                return Task.FromResult(true);
            }
        }
    }
}

