using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Reparatio.Repairs.Domain;

namespace Reparatio.Repairs.Infrastructure;

public sealed class SiteRow
{
    public Guid TenantId { get; set; }
    public Guid SiteId { get; set; }
    public long Version { get; set; }
    public long NextArrivalSequence { get; set; } = 1;
}
public sealed class TechnicianRow
{
    public Guid TenantId { get; set; }
    public Guid SiteId { get; set; }
    public Guid Id { get; set; }
    public bool IsAvailable { get; set; }
    public int ActiveRepairCount { get; set; }
    public DateTimeOffset? LastAssignedAt { get; set; }
    public TechnicianCandidate Snapshot() => new(Id, TenantId, SiteId, IsAvailable, ActiveRepairCount, LastAssignedAt);
}
public sealed class RepairRow
{
    public Guid TenantId { get; set; }
    public Guid SiteId { get; set; }
    public Guid Id { get; set; }
    public Guid? TechnicianId { get; set; }
    public RepairStatus Status { get; set; }
    public long ArrivalSequence { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
}
public sealed class ReceiptRow
{
    public Guid TenantId { get; set; }
    public string Operation { get; set; } = "";
    public Guid RequestId { get; set; }
    public Guid SiteId { get; set; }
    public string Payload { get; set; } = "";
    public string Result { get; set; } = "";
}
public sealed class ReassignmentRow
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RepairId { get; set; }
    public Guid PreviousTechnicianId { get; set; }
    public Guid NewTechnicianId { get; set; }
    public Guid ActorId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class RepairTransitionRow
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RepairId { get; set; }
    public string Operation { get; set; } = "";
    public RepairStatus PreviousStatus { get; set; }
    public RepairStatus NewStatus { get; set; }
    public Guid ActorId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public bool? Passed { get; set; }
    public string? Notes { get; set; }
}
public sealed class RepairsDbContext(DbContextOptions<RepairsDbContext> options) : DbContext(options)
{
    public DbSet<SiteRow> Sites => Set<SiteRow>();
    public DbSet<TechnicianRow> Technicians => Set<TechnicianRow>();
    public DbSet<RepairRow> Repairs => Set<RepairRow>();
    public DbSet<ReceiptRow> Receipts => Set<ReceiptRow>();
    public DbSet<ReassignmentRow> Reassignments => Set<ReassignmentRow>();

    public DbSet<RepairTransitionRow> RepairTransitions => Set<RepairTransitionRow>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var sites = model.Entity<SiteRow>();
        sites.ToTable("Sites", t => { t.HasCheckConstraint("CK_Site_Version", "[Version] >= 0"); t.HasCheckConstraint("CK_Site_Sequence", "[NextArrivalSequence] > 0"); });
        sites.HasKey(s => new { s.TenantId, s.SiteId });
        sites.Property(s => s.Version).IsConcurrencyToken();
        var tech = model.Entity<TechnicianRow>();
        tech.ToTable("Technicians", t => t.HasCheckConstraint("CK_Technician_Load", "[ActiveRepairCount] >= 0"));
        tech.HasKey(t => new { t.TenantId, t.SiteId, t.Id });
        tech.HasOne<SiteRow>().WithMany().HasForeignKey(t => new { t.TenantId, t.SiteId }).OnDelete(DeleteBehavior.Restrict);
        var repairs = model.Entity<RepairRow>();
        repairs.ToTable("Repairs", t => {
            t.HasCheckConstraint("CK_Repair_Sequence", "[ArrivalSequence] > 0");
            t.HasCheckConstraint("CK_Repair_Status", "[Status] BETWEEN 0 AND 8");
            t.HasCheckConstraint("CK_Repair_Assignment", "([Status] = 0 AND [TechnicianId] IS NULL) OR ([Status] <> 0 AND [TechnicianId] IS NOT NULL)");
        });
        repairs.HasKey(r => new { r.TenantId, r.Id });
        repairs.HasIndex(r => new { r.TenantId, r.SiteId, r.ArrivalSequence }).IsUnique();
        repairs.HasIndex(r => new { r.TenantId, r.SiteId, r.Status, r.ArrivalSequence });
        repairs.HasOne<SiteRow>().WithMany().HasForeignKey(r => new { r.TenantId, r.SiteId }).OnDelete(DeleteBehavior.Restrict);
        repairs.HasOne<TechnicianRow>().WithMany().HasForeignKey(r => new { r.TenantId, r.SiteId, Id = r.TechnicianId }).OnDelete(DeleteBehavior.Restrict);
        var receipts = model.Entity<ReceiptRow>();
        receipts.HasKey(r => new { r.TenantId, r.Operation, r.RequestId });
        receipts.Property(r => r.Operation).HasMaxLength(40);
        receipts.HasOne<SiteRow>().WithMany().HasForeignKey(r => new { r.TenantId, r.SiteId }).OnDelete(DeleteBehavior.Restrict);
        var transitions = model.Entity<RepairTransitionRow>();
        transitions.Property(t => t.Operation).HasMaxLength(40);
        transitions.Property(t => t.Notes).HasMaxLength(2000);
        transitions.HasIndex(t => new { t.TenantId, t.RepairId, t.Id });
        transitions.HasOne<RepairRow>().WithMany().HasForeignKey(t => new { t.TenantId, Id = t.RepairId }).OnDelete(DeleteBehavior.Restrict);
        var history = model.Entity<ReassignmentRow>();
        history.Property(r => r.Reason).HasMaxLength(1000);
        history.HasOne<RepairRow>().WithMany().HasForeignKey(r => new { r.TenantId, Id = r.RepairId }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RepairsDbContextFactory(string connection) : IDbContextFactory<RepairsDbContext>
{
    public RepairsDbContext CreateDbContext() => new(new DbContextOptionsBuilder<RepairsDbContext>().UseSqlServer(connection).Options);
}
public sealed class RepairsDesignTimeFactory : IDesignTimeDbContextFactory<RepairsDbContext>
{
    public RepairsDbContext CreateDbContext(string[] args)
    {
        var connection = SqlConnectionSettings.ReadRequired();
        return new RepairsDbContextFactory(connection).CreateDbContext();
    }
}
