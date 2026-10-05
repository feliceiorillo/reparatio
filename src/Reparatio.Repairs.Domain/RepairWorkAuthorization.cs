namespace Reparatio.Repairs.Domain;

public sealed record TechnicianReassignment(Guid PreviousTechnicianId, Guid NewTechnicianId,
    Guid ActorId, DateTimeOffset OccurredAt, string Reason);

public sealed record RepairWorkAuthorization
{
    public Guid AcceptedQuoteId { get; }
    public decimal RequiredDeposit { get; }
    public decimal ConfirmedPayments { get; }

    public RepairWorkAuthorization(Guid acceptedQuoteId, decimal requiredDeposit, decimal confirmedPayments)
    {
        if (acceptedQuoteId == Guid.Empty)
            throw new ArgumentException("An accepted quote is required.", nameof(acceptedQuoteId));
        ArgumentOutOfRangeException.ThrowIfNegative(requiredDeposit);
        ArgumentOutOfRangeException.ThrowIfNegative(confirmedPayments);
        AcceptedQuoteId = acceptedQuoteId;
        RequiredDeposit = requiredDeposit;
        ConfirmedPayments = confirmedPayments;
    }
}
