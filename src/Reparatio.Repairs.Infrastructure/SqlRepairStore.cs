using Microsoft.EntityFrameworkCore;
using Reparatio.Repairs.Application;
using Reparatio.Repairs.Domain;

namespace Reparatio.Repairs.Infrastructure;

public sealed class SqlRepairStore(IDbContextFactory<RepairsDbContext> factory) : IRepairOpeningStore, ITechnicianAvailabilityStore
{
    public Task<AssignmentSnapshot> ReadAsync(OpenRepairCommand command, CancellationToken cancellationToken)
        => Task.FromException<AssignmentSnapshot>(new NotImplementedException(factory.GetType().Name));
    public Task<bool> TryCommitAsync(OpenRepairCommand command, long expectedVersion, Repair repair,
        DateTimeOffset assignedAt, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<AvailabilitySnapshot> ReadAsync(SetTechnicianAvailabilityCommand command, CancellationToken cancellationToken)
        => throw new NotImplementedException();
    public Task<bool> TryCommitAsync(SetTechnicianAvailabilityCommand command, long expectedVersion,
        IReadOnlyList<WaitingAssignment> assignments, DateTimeOffset assignedAt, CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
