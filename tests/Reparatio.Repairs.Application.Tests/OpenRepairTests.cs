using Reparatio.Repairs.Application;
using Reparatio.Repairs.Domain;
using Xunit;

namespace Reparatio.Repairs.Application.Tests;

public class OpenRepairTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Site = Guid.NewGuid();
    private static OpenRepairCommand Command() => new(Guid.NewGuid(), Guid.NewGuid(), Tenant, Site);
    private static OpenRepairHandler Handler(Store store, bool allow = true)
        => new(new Access(allow), store, TimeProvider.System);

    [Fact]
    public async Task Opening_atomically_updates_workload_and_rotation()
    {
        var store = new Store();
        var result = await Handler(store).HandleAsync(Command());
        Assert.Equal(RepairStatus.AwaitingDiagnosis, result.Status);
        Assert.Equal(1, store.Candidates.Single(t => t.Id == result.TechnicianId).ActiveRepairCount);
        Assert.NotNull(store.Candidates.Single(t => t.Id == result.TechnicianId).LastAssignedAt);
        Assert.Single(store.Receipts);
    }

    [Fact]
    public async Task Repeating_request_does_not_open_or_assign_twice()
    {
        var store = new Store();
        var handler = Handler(store);
        var command = Command();
        var first = await handler.HandleAsync(command);
        Assert.Equal(first, await handler.HandleAsync(command));
        Assert.Single(store.Receipts);
        Assert.Equal(1, store.Candidates.Sum(t => t.ActiveRepairCount));
    }

    [Fact]
    public async Task Reusing_request_with_different_payload_is_rejected()
    {
        var store = new Store();
        var handler = Handler(store);
        var command = Command();
        await handler.HandleAsync(command);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(command with { RepairId = Guid.NewGuid() }));
        Assert.Single(store.Receipts);
    }

    [Fact]
    public async Task Conflicting_snapshot_is_retried_with_fresh_load()
    {
        var store = new Store { ConflictsRemaining = 1 };
        await Handler(store).HandleAsync(Command());
        Assert.Equal(2, store.CommitAttempts);
        Assert.Single(store.Receipts);
    }

    [Fact]
    public async Task Persistent_conflicts_have_bounded_retry_without_partial_write()
    {
        var store = new Store { ConflictsRemaining = 100 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(store).HandleAsync(Command()));
        Assert.Equal(5, store.CommitAttempts);
        Assert.Empty(store.Receipts);
        Assert.Equal(0, store.Candidates.Sum(t => t.ActiveRepairCount));
    }

    [Fact]
    public async Task Concurrent_openings_balance_load_and_concurrent_retries_are_idempotent()
    {
        var store = new Store();
        var command = Command();
        var handler = Handler(store);
        await Task.WhenAll(handler.HandleAsync(command), handler.HandleAsync(command), handler.HandleAsync(Command()));
        Assert.Equal(2, store.Receipts.Count);
        Assert.All(store.Candidates, t => Assert.Equal(1, t.ActiveRepairCount));
        Assert.True(store.CommitAttempts > 2);
    }

    [Fact]
    public async Task Authorization_precedes_storage_even_for_duplicate_requests()
    {
        var store = new Store();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Handler(store, false).HandleAsync(Command()));
        Assert.Equal(0, store.Reads);
    }

    [Fact]
    public async Task No_technicians_creates_waiting_repair_without_incrementing_workload()
    {
        var store = new Store();
        store.Candidates.Clear();
        var result = await Handler(store).HandleAsync(Command());
        Assert.Null(result.TechnicianId);
        Assert.Equal(RepairStatus.WaitingForAssignment, result.Status);
        Assert.Single(store.Receipts);
    }

    private sealed class Access(bool allow) : IRepairAccess
    {
        public Task EnsureCanReceiveAsync(Guid tenantId, Guid siteId, CancellationToken cancellationToken)
        {
            if (!allow) throw new UnauthorizedAccessException();
            return Task.CompletedTask;
        }
    }

    // Test adapter only: exercises compare-and-swap contract, not SQL transactions.
    private sealed class Store : IRepairOpeningStore
    {
        private readonly object gate = new();
        private long version;
        public int Reads { get; private set; }
        public int CommitAttempts { get; private set; }
        public int ConflictsRemaining { get; set; }
        public List<TechnicianCandidate> Candidates { get; } =
        [new(Guid.NewGuid(), Tenant, Site, true, 0, null), new(Guid.NewGuid(), Tenant, Site, true, 0, null)];
        public List<OpeningReceipt> Receipts { get; } = [];

        public async Task<AssignmentSnapshot> ReadAsync(OpenRepairCommand command, CancellationToken cancellationToken)
        {
            AssignmentSnapshot snapshot;
            lock (gate)
            {
                Reads++;
                snapshot = new(version, Candidates.ToArray(), Receipts.SingleOrDefault(r =>
                    r.Command.TenantId == command.TenantId && r.Command.RequestId == command.RequestId));
            }
            await Task.Yield(); // Allows several callers to read the same version before any commits.
            cancellationToken.ThrowIfCancellationRequested();
            return snapshot;
        }

        public Task<bool> TryCommitAsync(OpenRepairCommand command, long expectedVersion, Repair repair,
            DateTimeOffset assignedAt, CancellationToken cancellationToken)
        {
            lock (gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CommitAttempts++;
                if (ConflictsRemaining > 0) { ConflictsRemaining--; return Task.FromResult(false); }
                if (version != expectedVersion) return Task.FromResult(false);
                if (Receipts.Any(r => r.Command.TenantId == command.TenantId && r.Command.RepairId == command.RepairId))
                    throw new InvalidOperationException("Duplicate repair identifier.");
                if (repair.TechnicianId is { } technicianId)
                {
                    var index = Candidates.FindIndex(t => t.Id == technicianId);
                    var t = Candidates[index];
                    Candidates[index] = new(t.Id, t.TenantId, t.SiteId, t.IsAvailable, t.ActiveRepairCount + 1, assignedAt);
                }
                Receipts.Add(new(command, new(repair.Id, repair.TechnicianId, repair.Status)));
                version++;
                return Task.FromResult(true);
            }
        }
    }
}
