using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloudShell.Persistence.Migrations.CloudShell
{
    /// <inheritdoc />
    public partial class _20260613162026_AddResourceEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ResourceEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Timestamp = table.Column<long>(type: "INTEGER", nullable: false),
                    TriggeredBy = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Level = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    TraceId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    SpanId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceEvents_EventType",
                table: "ResourceEvents",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceEvents_ResourceId",
                table: "ResourceEvents",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceEvents_TraceId",
                table: "ResourceEvents",
                column: "TraceId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceEvents_Timestamp",
                table: "ResourceEvents",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResourceEvents");
        }
    }
}
