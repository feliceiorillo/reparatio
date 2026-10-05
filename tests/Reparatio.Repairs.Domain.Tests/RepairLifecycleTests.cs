using Reparatio.Repairs.Domain;
using Xunit;

namespace Reparatio.Repairs.Domain.Tests;

public class RepairLifecycleTests
{
    private readonly Guid tenant = Guid.NewGuid();
    private readonly Guid site = Guid.NewGuid();
    private readonly DateTimeOffset now = DateTimeOffset.Parse("2026-10-05T10:00:00Z");
    private TechnicianCandidate Tech(Guid? otherTenant = null, Guid? otherSite = null, bool available = true)
        => new(Guid.NewGuid(), otherTenant ?? tenant, otherSite ?? site, available, 0, null);
    private Repair Open() => Repair.Open(Guid.NewGuid(), tenant, site, [Tech()]);
    private static RepairWorkAuthorization Authorization(decimal required = 0, decimal paid = 0)
        => new(Guid.NewGuid(), required, paid);

    [Fact]
    public void Reassignment_records_previous_new_actor_time_and_reason()
    {
        var repair = Open();
        var previous = repair.TechnicianId;
        var next = Tech();
        var actor = Guid.NewGuid();
        repair.Reassign(next, actor, now, "Assenza");
        Assert.Equal(next.Id, repair.TechnicianId);
        Assert.Equal(RepairStatus.AwaitingDiagnosis, repair.Status);
        var entry = Assert.Single(repair.Reassignments);
        Assert.Equal(previous, entry.PreviousTechnicianId);
        Assert.Equal(next.Id, entry.NewTechnicianId);
        Assert.Equal(actor, entry.ActorId);
        Assert.Equal(now, entry.OccurredAt);
        Assert.Equal("Assenza", entry.Reason);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void Invalid_target_does_not_mutate_repair(bool otherTenant, bool otherSite, bool available)
    {
        var repair = Open();
        var previous = repair.TechnicianId;
        Assert.Throws<InvalidOperationException>(() => repair.Reassign(
            Tech(otherTenant ? Guid.NewGuid() : null, otherSite ? Guid.NewGuid() : null, available),
            Guid.NewGuid(), now, "Assenza"));
        Assert.Equal(previous, repair.TechnicianId);
        Assert.Empty(repair.Reassignments);
    }

    [Fact]
    public void Reassignment_requires_actor_reason_and_different_technician()
    {
        var repair = Open();
        Assert.Throws<ArgumentException>(() => repair.Reassign(Tech(), Guid.Empty, now, "Assenza"));
        Assert.Throws<ArgumentException>(() => repair.Reassign(Tech(), Guid.NewGuid(), now, " "));
        var same = new TechnicianCandidate(repair.TechnicianId!.Value, tenant, site, true, 1, now);
        Assert.Throws<InvalidOperationException>(() => repair.Reassign(same, Guid.NewGuid(), now, "Assenza"));
        Assert.Empty(repair.Reassignments);
    }

    [Fact]
    public void Positive_testing_removes_workload_and_reopening_restores_it()
    {
        var repair = Open();
        repair.StartWork(Authorization());
        repair.SubmitForTesting();
        repair.RecordTesting(true);
        Assert.Equal(RepairStatus.ReadyForCollection, repair.Status);
        Assert.False(RepairWorkload.Counts(repair.Status));
        repair.ReturnToWork();
        Assert.Equal(RepairStatus.InProgress, repair.Status);
        Assert.True(RepairWorkload.Counts(repair.Status));
    }

    [Fact]
    public void Failed_testing_returns_to_work_and_keeps_workload()
    {
        var repair = Open();
        repair.StartWork(Authorization());
        repair.SubmitForTesting();
        repair.RecordTesting(false);
        Assert.Equal(RepairStatus.InProgress, repair.Status);
        Assert.True(RepairWorkload.Counts(repair.Status));
    }

    [Fact]
    public void Work_requires_confirmed_deposit_and_assigned_technician()
    {
        var repair = Open();
        Assert.Throws<InvalidOperationException>(() => repair.StartWork(Authorization(60, 59)));
        Assert.Equal(RepairStatus.AwaitingDiagnosis, repair.Status);
        repair.StartWork(Authorization(60, 60));
        Assert.Equal(RepairStatus.InProgress, repair.Status);
        var waiting = Repair.Open(Guid.NewGuid(), tenant, site, []);
        Assert.Throws<InvalidOperationException>(() => waiting.StartWork(Authorization()));
    }

    [Fact]
    public void Testing_transitions_reject_invalid_source_states()
    {
        var repair = Open();
        Assert.Throws<InvalidOperationException>(() => repair.SubmitForTesting());
        Assert.Throws<InvalidOperationException>(() => repair.RecordTesting(true));
        Assert.Throws<InvalidOperationException>(() => repair.ReturnToWork());
        Assert.Equal(RepairStatus.AwaitingDiagnosis, repair.Status);
    }

    [Fact]
    public void Authorization_requires_accepted_quote_and_nonnegative_amounts()
    {
        Assert.Throws<ArgumentException>(() => new RepairWorkAuthorization(Guid.Empty, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Authorization(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Authorization(0, -1));
    }
}
