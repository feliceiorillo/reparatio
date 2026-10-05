using System.Text.Json.Serialization;
using Reparatio.Repairs.Domain;

namespace Reparatio.Repairs.Application;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$command")]
[JsonDerivedType(typeof(ReassignRepairCommand), "reassign")]
[JsonDerivedType(typeof(SubmitRepairForTestingCommand), "submit-testing")]
[JsonDerivedType(typeof(RecordRepairTestingCommand), "record-testing")]
[JsonDerivedType(typeof(ReturnRepairToWorkCommand), "return-work")]
public abstract record RepairLifecycleCommand(Guid RequestId, Guid TenantId, Guid SiteId, Guid RepairId)
{
    public abstract string Operation { get; }
}
public sealed record ReassignRepairCommand(Guid RequestId, Guid TenantId, Guid SiteId, Guid RepairId,
    Guid TechnicianId, string Reason) : RepairLifecycleCommand(RequestId, TenantId, SiteId, RepairId)
{ public override string Operation => "ReassignRepair"; }
public sealed record SubmitRepairForTestingCommand(Guid RequestId, Guid TenantId, Guid SiteId, Guid RepairId)
    : RepairLifecycleCommand(RequestId, TenantId, SiteId, RepairId)
{ public override string Operation => "SubmitRepairForTesting"; }
public sealed record RecordRepairTestingCommand(Guid RequestId, Guid TenantId, Guid SiteId, Guid RepairId,
    bool Passed, string? Notes = null) : RepairLifecycleCommand(RequestId, TenantId, SiteId, RepairId)
{ public override string Operation => "RecordRepairTesting"; }
public sealed record ReturnRepairToWorkCommand(Guid RequestId, Guid TenantId, Guid SiteId, Guid RepairId,
    string Reason) : RepairLifecycleCommand(RequestId, TenantId, SiteId, RepairId)
{ public override string Operation => "ReturnRepairToWork"; }
public sealed record RepairLifecycleResult(Guid RepairId, Guid? TechnicianId, RepairStatus Status);
public sealed record RepairLifecycleReceipt(RepairLifecycleCommand Command, RepairLifecycleResult Result);
public sealed record RepairLifecycleSnapshot(long Version, RepairState? Repair,
    IReadOnlyList<TechnicianCandidate> Candidates, RepairLifecycleReceipt? Receipt);

public interface IRepairLifecycleAccess
{
    // Implementations check authenticated permissions for the specific repair/action and return its actor identity.
    Task<Guid> EnsureCanReassignAsync(Guid tenantId, Guid siteId, Guid repairId, CancellationToken token);
    Task<Guid> EnsureCanTestAsync(Guid tenantId, Guid siteId, Guid repairId, CancellationToken token);
    Task<Guid> EnsureCanReturnToWorkAsync(Guid tenantId, Guid siteId, Guid repairId, CancellationToken token);
}
public interface IRepairLifecycleStore
{
    Task<RepairLifecycleSnapshot> ReadAsync(RepairLifecycleCommand command, CancellationToken token);
    // Same site version gate as opening/availability. Atomic repair, loads, audit and receipt; false => no writes.
    Task<bool> TryCommitAsync(RepairLifecycleCommand command, long expectedVersion, RepairState updated,
        Guid actorId, DateTimeOffset occurredAt, CancellationToken token);
}
public sealed class RepairLifecycleHandler(IRepairLifecycleAccess access, IRepairLifecycleStore store, TimeProvider clock)
{
    public Task<RepairLifecycleResult> HandleAsync(RepairLifecycleCommand command, CancellationToken token = default)
        => Task.FromException<RepairLifecycleResult>(new NotImplementedException($"{access}/{store}/{clock}"));
}
