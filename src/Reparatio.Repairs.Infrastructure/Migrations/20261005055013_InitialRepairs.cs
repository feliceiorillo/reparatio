using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reparatio.Repairs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialRepairs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sites",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    NextArrivalSequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sites", x => new { x.TenantId, x.SiteId });
                    table.CheckConstraint("CK_Site_Sequence", "[NextArrivalSequence] > 0");
                    table.CheckConstraint("CK_Site_Version", "[Version] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "Receipts",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Result = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Receipts", x => new { x.TenantId, x.Operation, x.RequestId });
                    table.ForeignKey(
                        name: "FK_Receipts_Sites_TenantId_SiteId",
                        columns: x => new { x.TenantId, x.SiteId },
                        principalTable: "Sites",
                        principalColumns: new[] { "TenantId", "SiteId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Technicians",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsAvailable = table.Column<bool>(type: "bit", nullable: false),
                    ActiveRepairCount = table.Column<int>(type: "int", nullable: false),
                    LastAssignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Technicians", x => new { x.TenantId, x.SiteId, x.Id });
                    table.CheckConstraint("CK_Technician_Load", "[ActiveRepairCount] >= 0");
                    table.ForeignKey(
                        name: "FK_Technicians_Sites_TenantId_SiteId",
                        columns: x => new { x.TenantId, x.SiteId },
                        principalTable: "Sites",
                        principalColumns: new[] { "TenantId", "SiteId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Repairs",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TechnicianId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ArrivalSequence = table.Column<long>(type: "bigint", nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Repairs", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Repair_Assignment", "([Status] = 0 AND [TechnicianId] IS NULL) OR ([Status] <> 0 AND [TechnicianId] IS NOT NULL)");
                    table.CheckConstraint("CK_Repair_Sequence", "[ArrivalSequence] > 0");
                    table.CheckConstraint("CK_Repair_Status", "[Status] BETWEEN 0 AND 8");
                    table.ForeignKey(
                        name: "FK_Repairs_Sites_TenantId_SiteId",
                        columns: x => new { x.TenantId, x.SiteId },
                        principalTable: "Sites",
                        principalColumns: new[] { "TenantId", "SiteId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Repairs_Technicians_TenantId_SiteId_TechnicianId",
                        columns: x => new { x.TenantId, x.SiteId, x.TechnicianId },
                        principalTable: "Technicians",
                        principalColumns: new[] { "TenantId", "SiteId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Reassignments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RepairId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousTechnicianId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NewTechnicianId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reassignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reassignments_Repairs_TenantId_RepairId",
                        columns: x => new { x.TenantId, x.RepairId },
                        principalTable: "Repairs",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reassignments_TenantId_RepairId",
                table: "Reassignments",
                columns: new[] { "TenantId", "RepairId" });

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_TenantId_SiteId",
                table: "Receipts",
                columns: new[] { "TenantId", "SiteId" });

            migrationBuilder.CreateIndex(
                name: "IX_Repairs_TenantId_SiteId_ArrivalSequence",
                table: "Repairs",
                columns: new[] { "TenantId", "SiteId", "ArrivalSequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Repairs_TenantId_SiteId_Status_ArrivalSequence",
                table: "Repairs",
                columns: new[] { "TenantId", "SiteId", "Status", "ArrivalSequence" });

            migrationBuilder.CreateIndex(
                name: "IX_Repairs_TenantId_SiteId_TechnicianId",
                table: "Repairs",
                columns: new[] { "TenantId", "SiteId", "TechnicianId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Reassignments");

            migrationBuilder.DropTable(
                name: "Receipts");

            migrationBuilder.DropTable(
                name: "Repairs");

            migrationBuilder.DropTable(
                name: "Technicians");

            migrationBuilder.DropTable(
                name: "Sites");
        }
    }
}
