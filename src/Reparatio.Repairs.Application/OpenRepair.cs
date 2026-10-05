using Reparatio.Repairs.Domain;

namespace Reparatio.Repairs.Application;

public sealed record OpenRepairCommand(Guid RequestId, Guid RepairId, Guid TenantId, Guid SiteId);
public sealed record OpenRepairResult(Guid RepairId, Guid? TechnicianId, RepairStatus Status);
public sealed record OpeningReceipt(OpenRepairCommand Command, OpenRepairResult Result);
public sealed record AssignmentSnapshot(long Version, IReadOnlyList<TechnicianCandidate> Candidates,
    OpeningReceipt? Receipt, int WaitingRepairCount = 0);

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
    public async Task<OpenRepairResult> HandleAsync(OpenRepairCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.RequestId == Guid.Empty || command.RepairId == Guid.Empty
            || command.TenantId == Guid.Empty || command.SiteId == Guid.Empty)
            throw new ArgumentException("All command identifiers are required.", nameof(command));
        cancellationToken.ThrowIfCancellationRequested();
        await access.EnsureCanReceiveAsync(command.TenantId, command.SiteId, cancellationToken);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await store.ReadAsync(command, cancellationToken);
            if (snapshot.Receipt is { } receipt)
            {
                if (receipt.Command != command)
                    throw new InvalidOperationException("Request identifier was already used for a different command.");
                return receipt.Result;
            }
            var repair = Repair.Open(command.RepairId, command.TenantId, command.SiteId, snapshot.Candidates);
            if (await store.TryCommitAsync(command, snapshot.Version, repair, clock.GetUtcNow(), cancellationToken))
                return new(repair.Id, repair.TechnicianId, repair.Status);
        }
        throw new InvalidOperationException("Concurrent assignment conflicts exceeded the retry limit; retry the same request.");
    }
}

