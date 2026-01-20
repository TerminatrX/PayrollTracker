using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PayrollManager.Domain.Migrations
{
    [DbContext(typeof(PayrollManager.Domain.Data.AppDbContext))]
    [Migration("20260120090000_AddEmployeeDefaultHoursPerPeriod")]
    public partial class AddEmployeeDefaultHoursPerPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultHoursPerPeriod",
                table: "Employees",
                type: "INTEGER",
                nullable: false,
                defaultValue: 80);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultHoursPerPeriod",
                table: "Employees");
        }
    }
}
