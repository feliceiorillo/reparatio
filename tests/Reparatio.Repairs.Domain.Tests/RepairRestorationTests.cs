using Reparatio.Repairs.Domain;
using Xunit;

namespace Reparatio.Repairs.Domain.Tests;

public class RepairRestorationTests
{
    [Fact]
    public void Restoration_keeps_status_technician_and_history_without_mutating_the_snapshot()
    {
        var tenant = Guid.NewGuid();
        var site = Guid.NewGuid();
        var technician = new TechnicianCandidate(Guid.NewGuid(), tenant, site, true, 0, null);
        var original = Repair.Open(Guid.NewGuid(), tenant, site, [technician]);
        original.Reassign(new(Guid.NewGuid(), tenant, site, true, 0, null), Guid.NewGuid(), DateTimeOffset.UtcNow, "Assenza");
        original.StartWork(new(Guid.NewGuid(), 0, 0));
        original.SubmitForTesting();
        var state = original.Snapshot();
        var restored = Repair.Restore(state);
        restored.RecordTesting(true);
        Assert.Equal(RepairStatus.AwaitingTesting, state.Status);
        Assert.Equal(original.TechnicianId, restored.TechnicianId);
        Assert.Equal(original.Reassignments, restored.Reassignments);
        Assert.Equal(RepairStatus.ReadyForCollection, restored.Status);
    }

    [Theory]
    [InlineData(RepairStatus.WaitingForAssignment, true)]
    [InlineData(RepairStatus.AwaitingDiagnosis, false)]
    [InlineData(RepairStatus.InProgress, false)]
    [InlineData(RepairStatus.ReadyForCollection, false)]
    public void Restoration_rejects_inconsistent_assignment(RepairStatus status, bool hasTechnician)
    {
        var state = new RepairState(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            hasTechnician ? Guid.NewGuid() : null, status, []);
        Assert.Throws<ArgumentException>(() => Repair.Restore(state));
    }

    [Fact]
    public void Restoration_copies_history_and_rejects_invalid_identifiers_or_status()
    {
        var history = new List<TechnicianReassignment>();
        var state = new RepairState(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), RepairStatus.InProgress, history);
        var repair = Repair.Restore(state);
        history.Add(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, "Assenza"));
        Assert.Empty(repair.Reassignments);
        Assert.Throws<ArgumentException>(() => Repair.Restore(state with { TenantId = Guid.Empty }));
        Assert.Throws<ArgumentException>(() => Repair.Restore(state with { Status = (RepairStatus)100 }));
    }
}
