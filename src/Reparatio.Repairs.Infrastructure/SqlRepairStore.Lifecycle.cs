using Microsoft.EntityFrameworkCore;
using Reparatio.Repairs.Application;
using Reparatio.Repairs.Domain;

namespace Reparatio.Repairs.Infrastructure;

public sealed partial class SqlRepairStore : IRepairLifecycleStore
{
    public Task<RepairLifecycleSnapshot> ReadAsync(RepairLifecycleCommand command, CancellationToken token)
        => throw new NotImplementedException();
    public Task<bool> TryCommitAsync(RepairLifecycleCommand command, long expectedVersion, RepairState updated,
        Guid actorId, DateTimeOffset occurredAt, CancellationToken token) => throw new NotImplementedException();
}
