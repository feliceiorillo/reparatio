using Reparatio.Repairs.Domain;
using Xunit;

namespace Reparatio.Repairs.Domain.Tests;

public class AssignmentTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Site = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-05T04:00:00Z");
    private static TechnicianCandidate Tech(int load = 0, DateTimeOffset? last = null,
        Guid? tenant = null, Guid? site = null, bool available = true, Guid? id = null)
        => new(id ?? Guid.NewGuid(), tenant ?? Tenant, site ?? Site, available, load, last);

    [Fact]
    public void Chooses_lowest_load_before_rotation()
    {
        var busy = Tech(3, Now.AddDays(-10));
        var free = Tech(1, Now);
        Assert.Equal(free.Id, TechnicianAssignmentPolicy.Select(Tenant, Site, [busy, free])?.Id);
    }

    [Fact]
    public void Equal_load_chooses_oldest_assignment()
    {
        var older = Tech(2, Now.AddDays(-2));
        Assert.Equal(older.Id, TechnicianAssignmentPolicy.Select(Tenant, Site, [Tech(2, Now), older])?.Id);
    }

    [Fact]
    public void Never_assigned_comes_before_previous_assignment()
    {
        var first = Tech(2);
        Assert.Equal(first.Id, TechnicianAssignmentPolicy.Select(Tenant, Site, [Tech(2, Now), first])?.Id);
    }

    [Fact]
    public void Exact_ties_use_stable_identifier_regardless_of_input_order()
    {
        var a = Tech(id: Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var b = Tech(id: Guid.Parse("00000000-0000-0000-0000-000000000002"));
        Assert.Equal(a.Id, TechnicianAssignmentPolicy.Select(Tenant, Site, [b, a])?.Id);
        Assert.Equal(a.Id, TechnicianAssignmentPolicy.Select(Tenant, Site, [a, b])?.Id);
    }

    [Fact]
    public void Excludes_other_tenants_sites_and_unavailable_technicians()
    {
        var eligible = Tech(5);
        Assert.Equal(eligible.Id, TechnicianAssignmentPolicy.Select(Tenant, Site,
            [Tech(tenant: Guid.NewGuid()), Tech(site: Guid.NewGuid()), Tech(available: false), eligible])?.Id);
    }

    [Fact]
    public void No_eligible_technician_leaves_new_repair_waiting()
    {
        var repair = Repair.Open(Guid.NewGuid(), Tenant, Site, [Tech(available: false)]);
        Assert.Null(repair.TechnicianId);
        Assert.Equal(RepairStatus.WaitingForAssignment, repair.Status);
    }

    [Fact]
    public void Opening_assigns_eligible_technician_and_waits_for_diagnosis()
    {
        var technician = Tech();
        var repair = Repair.Open(Guid.NewGuid(), Tenant, Site, [technician]);
        Assert.Equal(technician.Id, repair.TechnicianId);
        Assert.Equal(RepairStatus.AwaitingDiagnosis, repair.Status);
    }

    [Theory]
    [InlineData(RepairStatus.AwaitingDiagnosis, true)]
    [InlineData(RepairStatus.AwaitingCustomer, true)]
    [InlineData(RepairStatus.AwaitingDeposit, true)]
    [InlineData(RepairStatus.AwaitingPart, true)]
    [InlineData(RepairStatus.InProgress, true)]
    [InlineData(RepairStatus.AwaitingTesting, true)]
    [InlineData(RepairStatus.ReadyForCollection, false)]
    [InlineData(RepairStatus.Collected, false)]
    [InlineData(RepairStatus.ToReturnUnrepaired, false)]
    [InlineData(RepairStatus.WaitingForAssignment, false)]
    public void Workload_tracks_pending_work_not_collection(RepairStatus status, bool expected)
        => Assert.Equal(expected, RepairWorkload.Counts(status));

    [Fact]
    public void Negative_load_is_rejected()
        => Assert.Throws<ArgumentOutOfRangeException>(() => Tech(-1));
}
