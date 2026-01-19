using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PayrollManager.Domain.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanySettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FederalTaxPercent = table.Column<decimal>(type: "TEXT", nullable: false),
                    StateTaxPercent = table.Column<decimal>(type: "TEXT", nullable: false),
                    SocialSecurityPercent = table.Column<decimal>(type: "TEXT", nullable: false),
                    MedicarePercent = table.Column<decimal>(type: "TEXT", nullable: false),
                    PayPeriodsPerYear = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanySettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FirstName = table.Column<string>(type: "TEXT", nullable: false),
                    LastName = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsHourly = table.Column<bool>(type: "INTEGER", nullable: false),
                    AnnualSalary = table.Column<decimal>(type: "TEXT", nullable: false),
                    HourlyRate = table.Column<decimal>(type: "TEXT", nullable: false),
                    PreTax401kPercent = table.Column<decimal>(type: "TEXT", nullable: false),
                    HealthInsurancePerPeriod = table.Column<decimal>(type: "TEXT", nullable: false),
                    OtherDeductionsPerPeriod = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PeriodStart = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PayDate = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayStubs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EmployeeId = table.Column<int>(type: "INTEGER", nullable: false),
                    PayRunId = table.Column<int>(type: "INTEGER", nullable: false),
                    HoursWorked = table.Column<decimal>(type: "TEXT", nullable: false),
                    GrossPay = table.Column<decimal>(type: "TEXT", nullable: false),
                    PreTax401kDeduction = table.Column<decimal>(type: "TEXT", nullable: false),
                    TaxFederal = table.Column<decimal>(type: "TEXT", nullable: false),
                    TaxState = table.Column<decimal>(type: "TEXT", nullable: false),
                    TaxSocialSecurity = table.Column<decimal>(type: "TEXT", nullable: false),
                    TaxMedicare = table.Column<decimal>(type: "TEXT", nullable: false),
                    PostTaxDeductions = table.Column<decimal>(type: "TEXT", nullable: false),
                    NetPay = table.Column<decimal>(type: "TEXT", nullable: false),
                    YtdGross = table.Column<decimal>(type: "TEXT", nullable: false),
                    YtdNet = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayStubs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayStubs_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayStubs_PayRuns_PayRunId",
                        column: x => x.PayRunId,
                        principalTable: "PayRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayStubs_EmployeeId",
                table: "PayStubs",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayStubs_PayRunId",
                table: "PayStubs",
                column: "PayRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanySettings");

            migrationBuilder.DropTable(
                name: "PayStubs");

            migrationBuilder.DropTable(
                name: "Employees");

            migrationBuilder.DropTable(
                name: "PayRuns");
        }
    }
}
