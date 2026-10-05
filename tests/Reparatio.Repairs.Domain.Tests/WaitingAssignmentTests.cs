using Reparatio.Repairs.Domain;
using Xunit;

namespace Reparatio.Repairs.Domain.Tests;

public class WaitingAssignmentTests
{
    private readonly Guid tenant = Guid.NewGuid();
    private readonly Guid site = Guid.NewGuid();
    private readonly DateTimeOffset now = DateTimeOffset.Parse("2026-10-05T12:00:00Z");
    private TechnicianCandidate Tech(int load = 0, Guid? otherTenant = null, Guid? otherSite = null, bool available = true)
        => new(Guid.NewGuid(), otherTenant ?? tenant, otherSite ?? site, available, load, null);
    private WaitingRepairCandidate Waiting(long sequence, Guid? otherTenant = null, Guid? otherSite = null)
        => new(Guid.NewGuid(), otherTenant ?? tenant, otherSite ?? site, sequence);

    [Fact]
    public void Assigns_in_arrival_order_and_updates_load_between_selections()
    {
        var first = Waiting(1);
        var second = Waiting(2);
        var a = Tech();
        var b = Tech();
        var plan = WaitingAssignmentPolicy.Plan(tenant, site, [second, first], [a, b], now);
        Assert.Equal(new[] { first.RepairId, second.RepairId }, plan.Select(p => p.RepairId));
        Assert.Equal(2, plan.Select(p => p.TechnicianId).Distinct().Count());
    }

    [Fact]
    public void Excludes_foreign_repairs_and_technicians_and_unavailable_technicians()
    {
        var waiting = Waiting(3);
        var tech = Tech(5);
        var plan = WaitingAssignmentPolicy.Plan(tenant, site,
            [Waiting(1, otherTenant: Guid.NewGuid()), Waiting(2, otherSite: Guid.NewGuid()), waiting],
            [Tech(otherTenant: Guid.NewGuid()), Tech(otherSite: Guid.NewGuid()), Tech(available: false), tech], now);
        var assignment = Assert.Single(plan);
        Assert.Equal(waiting.RepairId, assignment.RepairId);
        Assert.Equal(tech.Id, assignment.TechnicianId);
    }

    [Fact]
    public void No_available_technicians_leaves_queue_unchanged()
        => Assert.Empty(WaitingAssignmentPolicy.Plan(tenant, site, [Waiting(1)], [Tech(available: false)], now));

    [Fact]
    public void Plan_does_not_mutate_input_snapshots()
    {
        var tech = Tech();
        WaitingAssignmentPolicy.Plan(tenant, site, [Waiting(1), Waiting(2)], [tech], now);
        Assert.Equal(0, tech.ActiveRepairCount);
        Assert.Null(tech.LastAssignedAt);
    }

    [Fact]
    public void Duplicate_queue_sequence_is_rejected_instead_of_inventing_arrival_order()
        => Assert.Throws<ArgumentException>(() => WaitingAssignmentPolicy.Plan(tenant, site,
            [Waiting(1), Waiting(1)], [Tech()], now));

    [Fact]
    public void Duplicate_repair_or_technician_is_rejected()
    {
        var repair = Waiting(1);
        var tech = Tech();
        Assert.Throws<ArgumentException>(() => WaitingAssignmentPolicy.Plan(tenant, site,
            [repair, repair with { ArrivalSequence = 2 }], [tech], now));
        Assert.Throws<ArgumentException>(() => WaitingAssignmentPolicy.Plan(tenant, site, [repair], [tech, tech], now));
    }

    [Fact]
    public void Waiting_repair_can_receive_its_first_technician_only_once()
    {
        var repair = Repair.Open(Guid.NewGuid(), tenant, site, []);
        var tech = Tech();
        repair.AssignWaiting(tech);
        Assert.Equal(tech.Id, repair.TechnicianId);
        Assert.Equal(RepairStatus.AwaitingDiagnosis, repair.Status);
        Assert.True(RepairWorkload.Counts(repair.Status));
        Assert.Empty(repair.Reassignments);
        Assert.Throws<InvalidOperationException>(() => repair.AssignWaiting(Tech()));
    }

    [Fact]
    public void Initial_assignment_rejects_foreign_or_unavailable_technician_without_mutation()
    {
        var repair = Repair.Open(Guid.NewGuid(), tenant, site, []);
        foreach (var tech in new[] { Tech(otherTenant: Guid.NewGuid()), Tech(otherSite: Guid.NewGuid()), Tech(available: false) })
            Assert.Throws<InvalidOperationException>(() => repair.AssignWaiting(tech));
        Assert.Null(repair.TechnicianId);
        Assert.Equal(RepairStatus.WaitingForAssignment, repair.Status);
    }
}
