using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloudShell.Persistence.Migrations.CloudShell
{
    /// <inheritdoc />
    public partial class _20260606113000_AddResourceRegistrationDependencies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DependsOnJson",
                table: "ResourceRegistrations",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DependsOnJson",
                table: "ResourceRegistrations");
        }
    }
}
