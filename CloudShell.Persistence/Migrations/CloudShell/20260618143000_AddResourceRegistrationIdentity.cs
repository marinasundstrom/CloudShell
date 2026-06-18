using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloudShell.Persistence.Migrations.CloudShell
{
    /// <inheritdoc />
    public partial class _20260618143000_AddResourceRegistrationIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdentityJson",
                table: "ResourceRegistrations",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IdentityJson",
                table: "ResourceRegistrations");
        }
    }
}
