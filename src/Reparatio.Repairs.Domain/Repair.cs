namespace Reparatio.Repairs.Domain;

public enum RepairStatus
{
    WaitingForAssignment,
    AwaitingDiagnosis,
    AwaitingCustomer,
    AwaitingDeposit,
    AwaitingPart,
    InProgress,
    AwaitingTesting,
    ReadyForCollection,
    Collected
}

public static class RepairWorkload
{
    public static bool Counts(RepairStatus status) => status switch
    {
        RepairStatus.AwaitingDiagnosis or RepairStatus.AwaitingCustomer
            or RepairStatus.AwaitingDeposit or RepairStatus.AwaitingPart
            or RepairStatus.InProgress or RepairStatus.AwaitingTesting => true,
        RepairStatus.WaitingForAssignment or RepairStatus.ReadyForCollection
            or RepairStatus.Collected => false,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };
}

public sealed class Repair
{
    public Guid Id { get; }
    public Guid TenantId { get; }
    public Guid SiteId { get; }
    public Guid? TechnicianId { get; }
    public RepairStatus Status { get; }

    private Repair(Guid id, Guid tenantId, Guid siteId, TechnicianCandidate? technician)
    {
        Id = id;
        TenantId = tenantId;
        SiteId = siteId;
        TechnicianId = technician?.Id;
        Status = technician is null ? RepairStatus.WaitingForAssignment : RepairStatus.AwaitingDiagnosis;
    }

    public static Repair Open(Guid id, Guid tenantId, Guid siteId,
        IEnumerable<TechnicianCandidate> candidates)
    {
        if (id == Guid.Empty) throw new ArgumentException("Repair identifier must not be empty.", nameof(id));
        return new Repair(id, tenantId, siteId,
            TechnicianAssignmentPolicy.Select(tenantId, siteId, candidates));
    }
}
