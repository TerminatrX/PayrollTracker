using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PayrollManager.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddW4AndEmploymentDateFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF scaffolds "" here, which cannot be parsed back into the enum and would throw
            // on the first read of any pre-existing employee row. "Single" is the same default
            // the entity uses, and is the conservative choice: it withholds at the highest of
            // the three schedules until a real Form W-4 is collected (W4OnFile stays false).
            migrationBuilder.AddColumn<string>(
                name: "FilingStatus",
                table: "Employees",
                type: "TEXT",
                nullable: false,
                defaultValue: "Single");

            migrationBuilder.AddColumn<DateTime>(
                name: "HireDate",
                table: "Employees",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IlAdditionalAllowances",
                table: "Employees",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "IlBasicAllowances",
                table: "Employees",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "TerminationDate",
                table: "Employees",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "W4Deductions",
                table: "Employees",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "W4DependentsAndOtherCredits",
                table: "Employees",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "W4ExtraWithholding",
                table: "Employees",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "W4MultipleJobsChecked",
                table: "Employees",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "W4OnFile",
                table: "Employees",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "W4OtherIncome",
                table: "Employees",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FilingStatus",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "HireDate",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "IlAdditionalAllowances",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "IlBasicAllowances",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "TerminationDate",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "W4Deductions",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "W4DependentsAndOtherCredits",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "W4ExtraWithholding",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "W4MultipleJobsChecked",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "W4OnFile",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "W4OtherIncome",
                table: "Employees");
        }
    }
}
