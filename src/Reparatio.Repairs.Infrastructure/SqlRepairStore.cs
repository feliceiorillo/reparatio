using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Reparatio.Repairs.Application;
using Reparatio.Repairs.Domain;

namespace Reparatio.Repairs.Infrastructure;

public sealed class SqlRepairStore(IDbContextFactory<RepairsDbContext> factory) : IRepairOpeningStore, ITechnicianAvailabilityStore
{
    private const string Opening = "OpenRepair";
    private const string Availability = "SetTechnicianAvailability";

    public async Task<AssignmentSnapshot> ReadAsync(OpenRepairCommand command, CancellationToken cancellationToken)
    {
        var data = await ReadCore(command.TenantId, command.SiteId, Opening, command.RequestId, cancellationToken);
        var receipt = data.Receipt is null ? null : new OpeningReceipt(
            Decode<OpenRepairCommand>(data.Receipt.Payload), Decode<OpenRepairResult>(data.Receipt.Result));
        return new(data.Version, data.Candidates, receipt, data.Waiting.Count);
    }

    public async Task<AvailabilitySnapshot> ReadAsync(SetTechnicianAvailabilityCommand command, CancellationToken cancellationToken)
    {
        var data = await ReadCore(command.TenantId, command.SiteId, Availability, command.RequestId, cancellationToken);
        var receipt = data.Receipt is null ? null : new AvailabilityReceipt(
            Decode<SetTechnicianAvailabilityCommand>(data.Receipt.Payload), Decode<AvailabilityResult>(data.Receipt.Result));
        return new(data.Version, data.Candidates, data.Waiting, receipt);
    }

    public Task<bool> TryCommitAsync(OpenRepairCommand command, long expectedVersion, Repair repair,
        DateTimeOffset assignedAt, CancellationToken cancellationToken)
        => Commit(command.TenantId, command.SiteId, expectedVersion, async db =>
        {
            if (repair.Id != command.RepairId || repair.TenantId != command.TenantId || repair.SiteId != command.SiteId)
                throw new InvalidOperationException("Repair does not match its command.");
            if (await db.Repairs.AnyAsync(r => r.TenantId == command.TenantId && r.Id == command.RepairId, cancellationToken))
                throw new InvalidOperationException("Repair identifier already exists in this tenant.");
            var technicians = await db.Technicians.Where(t => t.TenantId == command.TenantId && t.SiteId == command.SiteId)
                .ToListAsync(cancellationToken);
            var hasQueue = await db.Repairs.AnyAsync(r => r.TenantId == command.TenantId && r.SiteId == command.SiteId
                && r.Status == RepairStatus.WaitingForAssignment, cancellationToken);
            var expected = Repair.Open(command.RepairId, command.TenantId, command.SiteId,
                hasQueue ? [] : technicians.Select(t => t.Snapshot()));
            if (expected.TechnicianId != repair.TechnicianId || expected.Status != repair.Status)
                throw new InvalidOperationException("Repair assignment does not match current site state.");
            var site = await db.Sites.SingleAsync(s => s.TenantId == command.TenantId && s.SiteId == command.SiteId, cancellationToken);
            var sequence = site.NextArrivalSequence;
            site.NextArrivalSequence = checked(sequence + 1);
            db.Repairs.Add(new() { TenantId = command.TenantId, SiteId = command.SiteId, Id = command.RepairId,
                TechnicianId = repair.TechnicianId, Status = repair.Status, ArrivalSequence = sequence, OpenedAt = assignedAt });
            if (repair.TechnicianId is { } id) IncreaseLoad(technicians.Single(t => t.Id == id), assignedAt);
            db.Receipts.Add(Receipt(command.TenantId, command.SiteId, Opening, command.RequestId, command,
                new OpenRepairResult(repair.Id, repair.TechnicianId, repair.Status)));
        }, cancellationToken);

    public Task<bool> TryCommitAsync(SetTechnicianAvailabilityCommand command, long expectedVersion,
        IReadOnlyList<WaitingAssignment> assignments, DateTimeOffset assignedAt, CancellationToken cancellationToken)
        => Commit(command.TenantId, command.SiteId, expectedVersion, async db =>
        {
            var technicians = await db.Technicians.Where(t => t.TenantId == command.TenantId && t.SiteId == command.SiteId)
                .ToListAsync(cancellationToken);
            var target = technicians.SingleOrDefault(t => t.Id == command.TechnicianId)
                ?? throw new InvalidOperationException("Technician does not belong to this tenant and site.");
            target.IsAvailable = command.IsAvailable;
            var waiting = await db.Repairs.Where(r => r.TenantId == command.TenantId && r.SiteId == command.SiteId
                && r.Status == RepairStatus.WaitingForAssignment).OrderBy(r => r.ArrivalSequence).ToListAsync(cancellationToken);
            var expected = command.IsAvailable
                ? WaitingAssignmentPolicy.Plan(command.TenantId, command.SiteId,
                    waiting.Select(r => new WaitingRepairCandidate(r.Id, r.TenantId, r.SiteId, r.ArrivalSequence)),
                    technicians.Select(t => t.Snapshot()), assignedAt)
                : Array.AsReadOnly(Array.Empty<WaitingAssignment>());
            if (!expected.SequenceEqual(assignments))
                throw new InvalidOperationException("Assignment plan does not match current site state.");
            foreach (var assignment in assignments)
            {
                var row = waiting.Single(r => r.Id == assignment.RepairId);
                var technician = technicians.Single(t => t.Id == assignment.TechnicianId);
                var repair = Repair.Open(row.Id, row.TenantId, row.SiteId, []);
                repair.AssignWaiting(technician.Snapshot());
                row.TechnicianId = repair.TechnicianId;
                row.Status = repair.Status;
                IncreaseLoad(technician, assignedAt);
            }
            db.Receipts.Add(Receipt(command.TenantId, command.SiteId, Availability, command.RequestId, command,
                new AvailabilityResult(Array.AsReadOnly(assignments.ToArray()))));
        }, cancellationToken);

    private async Task<bool> Commit(Guid tenantId, Guid siteId, long version,
        Func<RepairsDbContext, Task> apply, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            // UPDATE obtains the site's write lock until commit: every writer uses this same gate.
            var changed = await db.Sites.Where(s => s.TenantId == tenantId && s.SiteId == siteId && s.Version == version)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Version, s => s.Version + 1), token);
            if (changed == 0) return false;
            await apply(db);
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return true;
        }
        catch (SqlException e) when (e.Number == 1205) { return false; }
        catch (DbUpdateException e) when (e.InnerException is SqlException sql && sql.Number is 1205 or 2601 or 2627)
        { return false; }
        // Disposal rolls back version and all writes on conflict/error/cancellation.
    }

    private sealed record ReadData(long Version, IReadOnlyList<TechnicianCandidate> Candidates,
        IReadOnlyList<WaitingRepairCandidate> Waiting, ReceiptRow? Receipt);

    private async Task<ReadData> ReadCore(Guid tenantId, Guid siteId, string operation, Guid requestId, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            await using var db = await factory.CreateDbContextAsync(token);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            try
            {
                // Read site's version first; its shared lock protects all subsequent reads from site writers.
                var site = await db.Sites.AsNoTracking().SingleOrDefaultAsync(s => s.TenantId == tenantId && s.SiteId == siteId, token)
                    ?? throw new InvalidOperationException("Site does not belong to this tenant.");
                var receipt = await db.Receipts.AsNoTracking().SingleOrDefaultAsync(r => r.TenantId == tenantId
                    && r.Operation == operation && r.RequestId == requestId, token);
                var technicians = await db.Technicians.AsNoTracking().Where(t => t.TenantId == tenantId && t.SiteId == siteId).ToListAsync(token);
                var waiting = await db.Repairs.AsNoTracking().Where(r => r.TenantId == tenantId && r.SiteId == siteId
                    && r.Status == RepairStatus.WaitingForAssignment).OrderBy(r => r.ArrivalSequence)
                    .Select(r => new WaitingRepairCandidate(r.Id, r.TenantId, r.SiteId, r.ArrivalSequence)).ToListAsync(token);
                await transaction.CommitAsync(token);
                return new(site.Version, technicians.Select(t => t.Snapshot()).ToArray(), waiting, receipt);
            }
            catch (SqlException e) when (e.Number == 1205 && attempt < 4) { }
        }
    }

    private static void IncreaseLoad(TechnicianRow technician, DateTimeOffset at)
    {
        technician.ActiveRepairCount = checked(technician.ActiveRepairCount + 1);
        technician.LastAssignedAt = at;
    }
    private static T Decode<T>(string json) => JsonSerializer.Deserialize<T>(json)
        ?? throw new InvalidOperationException("Invalid idempotency receipt.");
    private static ReceiptRow Receipt<TCommand, TResult>(Guid tenantId, Guid siteId, string operation,
        Guid requestId, TCommand command, TResult result) => new()
        { TenantId = tenantId, SiteId = siteId, Operation = operation, RequestId = requestId,
            Payload = JsonSerializer.Serialize(command), Result = JsonSerializer.Serialize(result) };
}
