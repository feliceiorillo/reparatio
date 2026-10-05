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
    public Task<AvailabilityResult> HandleAsync(SetTechnicianAvailabilityCommand command,
        CancellationToken cancellationToken = default)
        => Task.FromException<AvailabilityResult>(new NotImplementedException($"{access}/{store}/{clock}"));
}
