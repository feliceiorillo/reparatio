namespace Reparatio.Repairs.Domain;

// Sequence allocated monotonically and uniquely by the store within the site transaction.
public sealed record WaitingRepairCandidate(Guid RepairId, Guid TenantId, Guid SiteId, long ArrivalSequence);
public sealed record WaitingAssignment(Guid RepairId, Guid TechnicianId);

public static class WaitingAssignmentPolicy
{
    public static IReadOnlyList<WaitingAssignment> Plan(Guid tenantId, Guid siteId,
        IEnumerable<WaitingRepairCandidate> waiting, IEnumerable<TechnicianCandidate> technicians,
        DateTimeOffset assignedAt)
    {
        ArgumentNullException.ThrowIfNull(waiting);
        ArgumentNullException.ThrowIfNull(technicians);
        if (tenantId == Guid.Empty || siteId == Guid.Empty)
            throw new ArgumentException("Tenant and site are required.");
        var queue = waiting.Where(r => r.TenantId == tenantId && r.SiteId == siteId)
            .OrderBy(r => r.ArrivalSequence).ToArray();
        var candidates = technicians.Where(t => t.TenantId == tenantId && t.SiteId == siteId).ToList();
        if (queue.Any(r => r.RepairId == Guid.Empty || r.ArrivalSequence < 1)
            || queue.Select(r => r.RepairId).Distinct().Count() != queue.Length
            || queue.Select(r => r.ArrivalSequence).Distinct().Count() != queue.Length
            || candidates.Select(t => t.Id).Distinct().Count() != candidates.Count)
            throw new ArgumentException("Queue identifiers, arrival sequences and technician identities must be valid and unique.");
        var assignments = new List<WaitingAssignment>();
        foreach (var repair in queue)
        {
            var technician = TechnicianAssignmentPolicy.Select(tenantId, siteId, candidates);
            if (technician is null) break;
            assignments.Add(new(repair.RepairId, technician.Id));
            var index = candidates.IndexOf(technician);
            candidates[index] = new(technician.Id, tenantId, siteId, true,
                checked(technician.ActiveRepairCount + 1), assignedAt);
        }
        return assignments.AsReadOnly();
    }
}
