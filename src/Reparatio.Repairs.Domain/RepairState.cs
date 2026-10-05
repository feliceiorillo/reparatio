namespace Reparatio.Repairs.Domain;

public sealed record RepairState(Guid Id, Guid TenantId, Guid SiteId, Guid? TechnicianId,
    RepairStatus Status, IReadOnlyList<TechnicianReassignment> Reassignments);
