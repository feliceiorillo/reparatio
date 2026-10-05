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
    public Guid? TechnicianId { get; private set; }
    public RepairStatus Status { get; private set; }

    private readonly List<TechnicianReassignment> reassignments = [];
    public IReadOnlyList<TechnicianReassignment> Reassignments => reassignments.AsReadOnly();

    public void AssignWaiting(TechnicianCandidate technician) => throw new NotImplementedException();

    public void Reassign(TechnicianCandidate technician, Guid actorId,
        DateTimeOffset occurredAt, string reason)
    {
        ArgumentNullException.ThrowIfNull(technician);
        if (actorId == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actorId));
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (technician.TenantId != TenantId || technician.SiteId != SiteId || !technician.IsAvailable)
            throw new InvalidOperationException("Technician must be available in the same tenant and site.");
        if (TechnicianId is null || TechnicianId == technician.Id || !RepairWorkload.Counts(Status))
            throw new InvalidOperationException("Only assigned repairs with pending work can be reassigned.");
        reassignments.Add(new(TechnicianId.Value, technician.Id, actorId, occurredAt, reason.Trim()));
        TechnicianId = technician.Id;
    }

    // Trusted snapshot from Quotes/Payments; the application must verify access and read it consistently.
    public void StartWork(RepairWorkAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        if (TechnicianId is null || Status is not (RepairStatus.AwaitingDiagnosis
            or RepairStatus.AwaitingCustomer or RepairStatus.AwaitingDeposit or RepairStatus.AwaitingPart))
            throw new InvalidOperationException("Repair cannot start work in this state.");
        if (authorization.ConfirmedPayments < authorization.RequiredDeposit)
            throw new InvalidOperationException("The agreed deposit has not been confirmed.");
        Status = RepairStatus.InProgress;
    }

    public void SubmitForTesting()
    {
        RequireStatus(RepairStatus.InProgress);
        Status = RepairStatus.AwaitingTesting;
    }

    public void RecordTesting(bool passed)
    {
        RequireStatus(RepairStatus.AwaitingTesting);
        Status = passed ? RepairStatus.ReadyForCollection : RepairStatus.InProgress;
    }

    public void ReturnToWork()
    {
        RequireStatus(RepairStatus.ReadyForCollection);
        Status = RepairStatus.InProgress;
    }

    private void RequireStatus(RepairStatus expected)
    {
        if (Status != expected) throw new InvalidOperationException($"Expected repair state {expected}.");
    }

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


