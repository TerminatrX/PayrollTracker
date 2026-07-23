using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PayrollManager.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeAddressAndSsn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Employees",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "Employees",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SsnEncrypted",
                table: "Employees",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SsnLast4",
                table: "Employees",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "Employees",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StreetAddress",
                table: "Employees",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "City",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "SsnEncrypted",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "SsnLast4",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "State",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "StreetAddress",
                table: "Employees");
        }
    }
}
