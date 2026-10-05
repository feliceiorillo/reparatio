using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reparatio.Repairs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StaffAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StaffIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Issuer = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffIdentities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StaffGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TechnicianId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Role = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffGrants", x => x.Id);
                    table.CheckConstraint("CK_StaffGrant_Role", "[Role] BETWEEN 0 AND 3");
                    table.CheckConstraint("CK_StaffGrant_Scope", "([Role] = 0 AND [SiteId] IS NULL AND [TechnicianId] IS NULL) OR ([Role] IN (1,2) AND [SiteId] IS NOT NULL AND [TechnicianId] IS NULL) OR ([Role] = 3 AND [SiteId] IS NOT NULL AND [TechnicianId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_StaffGrants_Sites_TenantId_SiteId",
                        columns: x => new { x.TenantId, x.SiteId },
                        principalTable: "Sites",
                        principalColumns: new[] { "TenantId", "SiteId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffGrants_StaffIdentities_UserId",
                        column: x => x.UserId,
                        principalTable: "StaffIdentities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffGrants_Technicians_TenantId_SiteId_TechnicianId",
                        columns: x => new { x.TenantId, x.SiteId, x.TechnicianId },
                        principalTable: "Technicians",
                        principalColumns: new[] { "TenantId", "SiteId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffGrants_TenantId_SiteId_TechnicianId",
                table: "StaffGrants",
                columns: new[] { "TenantId", "SiteId", "TechnicianId" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffGrants_UserId_TenantId_SiteId",
                table: "StaffGrants",
                columns: new[] { "UserId", "TenantId", "SiteId" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffIdentities_Issuer_Subject",
                table: "StaffIdentities",
                columns: new[] { "Issuer", "Subject" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StaffGrants");

            migrationBuilder.DropTable(
                name: "StaffIdentities");
        }
    }
}
