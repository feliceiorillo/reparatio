using Reparatio.Repairs.Domain;

namespace Reparatio.Repairs.Application;

public sealed record OpenRepairCommand(Guid RequestId, Guid RepairId, Guid TenantId, Guid SiteId);
public sealed record OpenRepairResult(Guid RepairId, Guid? TechnicianId, RepairStatus Status);
public sealed record OpeningReceipt(OpenRepairCommand Command, OpenRepairResult Result);
public sealed record AssignmentSnapshot(long Version, IReadOnlyList<TechnicianCandidate> Candidates,
    OpeningReceipt? Receipt);

public interface IRepairAccess
{
    // Implementation uses the authenticated identity, never a client-supplied role.
    Task EnsureCanReceiveAsync(Guid tenantId, Guid siteId, CancellationToken cancellationToken);
}

public interface IRepairOpeningStore
{
    Task<AssignmentSnapshot> ReadAsync(OpenRepairCommand command, CancellationToken cancellationToken);

    // Atomically compare the tenant/site assignment version, enforce request and repair uniqueness,
    // insert repair+receipt, update assigned technician workload/last assignment, and increment version.
    // All assignment-affecting writers must participate in the same version protocol.
    // A conflicting version or concurrently completed identical request returns false with no mutation.
    Task<bool> TryCommitAsync(OpenRepairCommand command, long expectedVersion, Repair repair,
        DateTimeOffset assignedAt, CancellationToken cancellationToken);
}

public sealed class OpenRepairHandler(IRepairAccess access, IRepairOpeningStore store, TimeProvider clock)
{
    public Task<OpenRepairResult> HandleAsync(OpenRepairCommand command, CancellationToken cancellationToken = default)
        => Task.FromException<OpenRepairResult>(new NotImplementedException($"{access.GetType().Name}/{store.GetType().Name}/{clock.GetType().Name}"));
}

