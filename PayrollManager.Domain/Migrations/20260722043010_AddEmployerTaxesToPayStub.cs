using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PayrollManager.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployerTaxesToPayStub : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "EmployerFuta",
                table: "PayStubs",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "EmployerMedicare",
                table: "PayStubs",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "EmployerSocialSecurity",
                table: "PayStubs",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "EmployerSui",
                table: "PayStubs",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmployerFuta",
                table: "PayStubs");

            migrationBuilder.DropColumn(
                name: "EmployerMedicare",
                table: "PayStubs");

            migrationBuilder.DropColumn(
                name: "EmployerSocialSecurity",
                table: "PayStubs");

            migrationBuilder.DropColumn(
                name: "EmployerSui",
                table: "PayStubs");
        }
    }
}
