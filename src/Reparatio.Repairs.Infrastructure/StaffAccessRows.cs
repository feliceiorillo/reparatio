namespace Reparatio.Repairs.Infrastructure;

public enum StaffRole { TenantAdministrator, SiteManager, Receptionist, Technician }
public sealed class StaffIdentityRow
{
    public Guid Id { get; set; }
    public string Issuer { get; set; } = "";
    public string Subject { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
public sealed class StaffGrantRow
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public Guid? SiteId { get; set; }
    public Guid? TechnicianId { get; set; }
    public StaffRole Role { get; set; }
}
