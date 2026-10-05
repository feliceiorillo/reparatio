using Microsoft.EntityFrameworkCore;
using Reparatio.Repairs.Application;
using Reparatio.Repairs.Domain;

namespace Reparatio.Repairs.Infrastructure;

public sealed partial class SqlRepairStore : IRepairLifecycleStore
{
    public async Task<RepairLifecycleSnapshot> ReadAsync(RepairLifecycleCommand command, CancellationToken token)
    {
        RepairLifecycleBehavior.Validate(command);
        var data = await ReadCore(command.TenantId, command.SiteId, command.Operation, command.RequestId, token, command.RepairId);
        var receipt = data.Receipt is null ? null : new RepairLifecycleReceipt(
            Decode<RepairLifecycleCommand>(data.Receipt.Payload), Decode<RepairLifecycleResult>(data.Receipt.Result));
        return new(data.Version, data.Repair, data.Candidates, receipt);
    }

    public Task<bool> TryCommitAsync(RepairLifecycleCommand command, long expectedVersion, RepairState updated,
        Guid actorId, DateTimeOffset occurredAt, CancellationToken token)
        => Commit(command.TenantId, command.SiteId, expectedVersion, async db =>
        {
            var row = await db.Repairs.SingleOrDefaultAsync(r => r.TenantId == command.TenantId
                && r.SiteId == command.SiteId && r.Id == command.RepairId, token)
                ?? throw new InvalidOperationException("Repair does not belong to this tenant and site.");
            var before = await ReadRepairState(db, row, token);
            var technicians = await db.Technicians.Where(t => t.TenantId == command.TenantId && t.SiteId == command.SiteId).ToListAsync(token);
            var repair = Repair.Restore(before);
            RepairLifecycleBehavior.Apply(repair, command, technicians.Select(t => t.Snapshot()).ToArray(), actorId, occurredAt);
            var expected = repair.Snapshot();
            if (expected.Id != updated.Id || expected.TenantId != updated.TenantId || expected.SiteId != updated.SiteId
                || expected.TechnicianId != updated.TechnicianId || expected.Status != updated.Status
                || !expected.Reassignments.SequenceEqual(updated.Reassignments))
                throw new InvalidOperationException("Repair change does not match current persistent state.");

            var countedBefore = RepairWorkload.Counts(before.Status);
            var countedAfter = RepairWorkload.Counts(expected.Status);
            var changedTechnician = before.TechnicianId != expected.TechnicianId;
            if (countedBefore && (!countedAfter || changedTechnician))
            {
                var previous = technicians.Single(t => t.Id == before.TechnicianId);
                if (previous.ActiveRepairCount < 1)
                    throw new InvalidOperationException("Persistent technician workload is inconsistent with this repair.");
                previous.ActiveRepairCount--;
            }
            if (countedAfter && (!countedBefore || changedTechnician))
            {
                var next = technicians.Single(t => t.Id == expected.TechnicianId);
                next.ActiveRepairCount = checked(next.ActiveRepairCount + 1);
                // Reopening adds workload, but is not a new assignment for rotation.
                if (changedTechnician) next.LastAssignedAt = occurredAt;
            }
            row.TechnicianId = expected.TechnicianId;
            row.Status = expected.Status;
            foreach (var entry in expected.Reassignments.Skip(before.Reassignments.Count))
                db.Reassignments.Add(new() { TenantId = command.TenantId, RepairId = command.RepairId,
                    PreviousTechnicianId = entry.PreviousTechnicianId, NewTechnicianId = entry.NewTechnicianId,
                    ActorId = entry.ActorId, OccurredAt = entry.OccurredAt, Reason = entry.Reason });
            if (command is not ReassignRepairCommand)
                db.RepairTransitions.Add(new() { TenantId = command.TenantId, RepairId = command.RepairId,
                    Operation = command.Operation, PreviousStatus = before.Status, NewStatus = expected.Status,
                    ActorId = actorId, OccurredAt = occurredAt,
                    Passed = command is RecordRepairTestingCommand testing ? testing.Passed : null,
                    Notes = command switch { RecordRepairTestingCommand c => c.Notes?.Trim(), ReturnRepairToWorkCommand c => c.Reason.Trim(), _ => null } });
            db.Receipts.Add(Receipt<RepairLifecycleCommand, RepairLifecycleResult>(command.TenantId, command.SiteId,
                command.Operation, command.RequestId, command, new(expected.Id, expected.TechnicianId, expected.Status)));
        }, token);

    private static async Task<RepairState> ReadRepairState(RepairsDbContext db, RepairRow row, CancellationToken token)
    {
        var history = await db.Reassignments.AsNoTracking().Where(r => r.TenantId == row.TenantId && r.RepairId == row.Id)
            .OrderBy(r => r.Id).Select(r => new TechnicianReassignment(r.PreviousTechnicianId, r.NewTechnicianId,
                r.ActorId, r.OccurredAt, r.Reason)).ToListAsync(token);
        return new(row.Id, row.TenantId, row.SiteId, row.TechnicianId, row.Status, history.AsReadOnly());
    }
}
