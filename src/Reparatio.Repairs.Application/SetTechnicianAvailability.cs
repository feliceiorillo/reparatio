using Reparatio.Repairs.Domain;

namespace Reparatio.Repairs.Application;

public sealed record SetTechnicianAvailabilityCommand(Guid RequestId, Guid TenantId, Guid SiteId,
    Guid TechnicianId, bool IsAvailable);
public sealed record AvailabilityResult(IReadOnlyList<WaitingAssignment> Assignments);
public sealed record AvailabilityReceipt(SetTechnicianAvailabilityCommand Command, AvailabilityResult Result);
public sealed record AvailabilitySnapshot(long Version, IReadOnlyList<TechnicianCandidate> Candidates,
    IReadOnlyList<WaitingRepairCandidate> Waiting, AvailabilityReceipt? Receipt);

public interface ITechnicianAvailabilityAccess
{
    Task EnsureCanManageAsync(Guid tenantId, Guid siteId, CancellationToken cancellationToken);
}

public interface ITechnicianAvailabilityStore
{
    Task<AvailabilitySnapshot> ReadAsync(SetTechnicianAvailabilityCommand command, CancellationToken cancellationToken);

    // Same site version protocol as opening. Atomic: availability, assignments, repair states,
    // workload/last assignment, queue removal, receipt, version. false => no changes.
    // Revalidate every repair is still waiting and each technician belongs to this site.
    Task<bool> TryCommitAsync(SetTechnicianAvailabilityCommand command, long expectedVersion,
        IReadOnlyList<WaitingAssignment> assignments, DateTimeOffset assignedAt, CancellationToken cancellationToken);
}

public sealed class SetTechnicianAvailabilityHandler(ITechnicianAvailabilityAccess access,
    ITechnicianAvailabilityStore store, TimeProvider clock)
{
    public async Task<AvailabilityResult> HandleAsync(SetTechnicianAvailabilityCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.RequestId == Guid.Empty || command.TenantId == Guid.Empty
            || command.SiteId == Guid.Empty || command.TechnicianId == Guid.Empty)
            throw new ArgumentException("All identifiers are required.", nameof(command));
        cancellationToken.ThrowIfCancellationRequested();
        await access.EnsureCanManageAsync(command.TenantId, command.SiteId, cancellationToken);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await store.ReadAsync(command, cancellationToken);
            if (snapshot.Receipt is { } receipt)
            {
                if (receipt.Command != command)
                    throw new InvalidOperationException("Request identifier was reused with a different payload.");
                return receipt.Result;
            }
            var target = snapshot.Candidates.SingleOrDefault(t => t.Id == command.TechnicianId
                && t.TenantId == command.TenantId && t.SiteId == command.SiteId)
                ?? throw new InvalidOperationException("Technician does not belong to this tenant and site.");
            var candidates = snapshot.Candidates.Select(t => t == target
                ? new TechnicianCandidate(t.Id, t.TenantId, t.SiteId, command.IsAvailable,
                    t.ActiveRepairCount, t.LastAssignedAt) : t).ToArray();
            var assignedAt = clock.GetUtcNow();
            var assignments = command.IsAvailable
                ? WaitingAssignmentPolicy.Plan(command.TenantId, command.SiteId, snapshot.Waiting, candidates, assignedAt)
                : Array.AsReadOnly(Array.Empty<WaitingAssignment>());
            if (await store.TryCommitAsync(command, snapshot.Version, assignments, assignedAt, cancellationToken))
                return new(assignments);
        }
        throw new InvalidOperationException("Concurrent assignment conflicts exceeded the retry limit; retry the same request.");
    }
}
