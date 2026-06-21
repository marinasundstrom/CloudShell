using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloudShell.Persistence.Migrations.CloudShell
{
    /// <inheritdoc />
    public partial class _20260621102255_AddTelemetryPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TelemetryMetricPoints",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ServiceName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Value = table.Column<double>(type: "REAL", nullable: false),
                    Timestamp = table.Column<long>(type: "INTEGER", nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    AttributesJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryMetricPoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TelemetryTraceSpans",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TraceId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SpanId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ParentSpanId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ServiceName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    StartTime = table.Column<long>(type: "INTEGER", nullable: false),
                    DurationTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    AttributesJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryTraceSpans", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryMetricPoints_Name",
                table: "TelemetryMetricPoints",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryMetricPoints_ResourceId",
                table: "TelemetryMetricPoints",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryMetricPoints_Timestamp",
                table: "TelemetryMetricPoints",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryTraceSpans_ResourceId",
                table: "TelemetryTraceSpans",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryTraceSpans_StartTime",
                table: "TelemetryTraceSpans",
                column: "StartTime");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryTraceSpans_TraceId",
                table: "TelemetryTraceSpans",
                column: "TraceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TelemetryMetricPoints");

            migrationBuilder.DropTable(
                name: "TelemetryTraceSpans");
        }
    }
}
