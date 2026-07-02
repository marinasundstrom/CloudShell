using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloudShell.Persistence.Migrations.CloudShell
{
    /// <inheritdoc />
    public partial class _20260702164000_AddUsagePersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UsageSamples",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Value = table.Column<double>(type: "REAL", nullable: false),
                    Timestamp = table.Column<long>(type: "INTEGER", nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    AttributesJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageSamples", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UsageSamples_Name",
                table: "UsageSamples",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_UsageSamples_ResourceId",
                table: "UsageSamples",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_UsageSamples_Timestamp",
                table: "UsageSamples",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_UsageSamples_ResourceId_Name_Timestamp",
                table: "UsageSamples",
                columns: new[] { "ResourceId", "Name", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UsageSamples");
        }
    }
}
