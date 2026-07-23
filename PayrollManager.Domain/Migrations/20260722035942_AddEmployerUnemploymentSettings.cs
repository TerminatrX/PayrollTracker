using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PayrollManager.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployerUnemploymentSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "StateTaxPercent",
                table: "CompanySettings",
                newName: "SuiWageBase");

            migrationBuilder.RenameColumn(
                name: "FederalTaxPercent",
                table: "CompanySettings",
                newName: "SuiRatePercent");

            // EF scaffolds false, but the entity default is true and false is the expensive
            // wrong answer: it computes FUTA at the full 6.0% gross rate instead of the 0.6%
            // net rate, overstating employer unemployment cost roughly tenfold. Illinois
            // employers in good standing receive the full state credit.
            migrationBuilder.AddColumn<bool>(
                name: "ReceivesFullFutaCredit",
                table: "CompanySettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReceivesFullFutaCredit",
                table: "CompanySettings");

            migrationBuilder.RenameColumn(
                name: "SuiWageBase",
                table: "CompanySettings",
                newName: "StateTaxPercent");

            migrationBuilder.RenameColumn(
                name: "SuiRatePercent",
                table: "CompanySettings",
                newName: "FederalTaxPercent");
        }
    }
}
