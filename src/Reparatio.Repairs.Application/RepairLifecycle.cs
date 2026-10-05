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
    public async Task<RepairLifecycleResult> HandleAsync(RepairLifecycleCommand command, CancellationToken token = default)
    {
        RepairLifecycleBehavior.Validate(command);
        token.ThrowIfCancellationRequested();
        var actor = command switch
        {
            ReassignRepairCommand => await access.EnsureCanReassignAsync(command.TenantId, command.SiteId, command.RepairId, token),
            SubmitRepairForTestingCommand or RecordRepairTestingCommand => await access.EnsureCanTestAsync(command.TenantId, command.SiteId, command.RepairId, token),
            ReturnRepairToWorkCommand => await access.EnsureCanReturnToWorkAsync(command.TenantId, command.SiteId, command.RepairId, token),
            _ => throw new ArgumentException("Unsupported repair command.", nameof(command))
        };
        if (actor == Guid.Empty) throw new UnauthorizedAccessException("An authenticated actor is required.");
        for (var attempt = 0; attempt < 5; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var snapshot = await store.ReadAsync(command, token);
            if (snapshot.Receipt is { } receipt)
            {
                if (receipt.Command != command) throw new InvalidOperationException("Request identifier was reused with another payload.");
                return receipt.Result;
            }
            var state = snapshot.Repair ?? throw new InvalidOperationException("Repair does not belong to this tenant and site.");
            var repair = Repair.Restore(state);
            var at = clock.GetUtcNow();
            RepairLifecycleBehavior.Apply(repair, command, snapshot.Candidates, actor, at);
            if (await store.TryCommitAsync(command, snapshot.Version, repair.Snapshot(), actor, at, token))
                return new(repair.Id, repair.TechnicianId, repair.Status);
        }
        throw new InvalidOperationException("Concurrent repair conflicts exceeded the retry limit; retry the same request.");
    }
}

// Shared pure translator used by Application and by persistence to revalidate the plan under the site lock.
public static class RepairLifecycleBehavior
{
    public static void Validate(RepairLifecycleCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.RequestId == Guid.Empty || command.TenantId == Guid.Empty || command.SiteId == Guid.Empty || command.RepairId == Guid.Empty)
            throw new ArgumentException("All identifiers are required.", nameof(command));
        switch (command)
        {
            case ReassignRepairCommand c:
                if (c.TechnicianId == Guid.Empty) throw new ArgumentException("Target technician is required.");
                Reason(c.Reason, 1000);
                break;
            case ReturnRepairToWorkCommand c: Reason(c.Reason, 2000); break;
            case RecordRepairTestingCommand c:
                if (c.Notes?.Length > 2000) throw new ArgumentException("Testing notes are limited to 2000 characters.");
                break;
            case SubmitRepairForTestingCommand: break;
            default: throw new ArgumentException("Unsupported repair command.", nameof(command));
        }
    }
    private static void Reason(string reason, int maxLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (reason.Trim().Length > maxLength) throw new ArgumentException($"Reason is limited to {maxLength} characters.");
    }
    public static void Apply(Repair repair, RepairLifecycleCommand command,
        IReadOnlyList<TechnicianCandidate> candidates, Guid actorId, DateTimeOffset at)
    {
        Validate(command);
        if (actorId == Guid.Empty) throw new ArgumentException("Authenticated actor is required.", nameof(actorId));
        if (repair.Id != command.RepairId || repair.TenantId != command.TenantId || repair.SiteId != command.SiteId)
            throw new InvalidOperationException("Repair does not belong to this command scope.");
        switch (command)
        {
            case ReassignRepairCommand c:
                var technician = candidates.SingleOrDefault(t => t.Id == c.TechnicianId && t.TenantId == c.TenantId && t.SiteId == c.SiteId)
                    ?? throw new InvalidOperationException("Target technician does not belong to this tenant and site.");
                repair.Reassign(technician, actorId, at, c.Reason);
                break;
            case SubmitRepairForTestingCommand: repair.SubmitForTesting(); break;
            case RecordRepairTestingCommand c: repair.RecordTesting(c.Passed); break;
            case ReturnRepairToWorkCommand: repair.ReturnToWork(); break;
        }
    }
}
