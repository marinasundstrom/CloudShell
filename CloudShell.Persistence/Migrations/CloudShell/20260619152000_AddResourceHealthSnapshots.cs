using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloudShell.Persistence.Migrations.CloudShell
{
    /// <inheritdoc />
    public partial class _20260619152000_AddResourceHealthSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ResourceHealthSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CheckedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ChecksJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceHealthSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceHealthSnapshots_CheckedAt",
                table: "ResourceHealthSnapshots",
                column: "CheckedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceHealthSnapshots_ResourceId",
                table: "ResourceHealthSnapshots",
                column: "ResourceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResourceHealthSnapshots");
        }
    }
}
