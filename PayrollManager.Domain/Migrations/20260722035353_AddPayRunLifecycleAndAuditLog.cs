using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PayrollManager.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddPayRunLifecycleAndAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CalculationEngineVersion",
                table: "PayRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CalculationHash",
                table: "PayRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PostedAtUtc",
                table: "PayRuns",
                type: "TEXT",
                nullable: true);

            // EF scaffolds "" here, which cannot be parsed back into PayRunStatus and would
            // throw on the first read of any existing pay run.
            //
            // "Posted" is the correct backfill: every pre-existing run already produced pay
            // stubs, so it represents committed payroll and must become immutable rather than
            // re-editable. It also makes those periods participate in the overlap check.
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "PayRuns",
                type: "TEXT",
                nullable: false,
                defaultValue: "Posted");

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "PayRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoidedAtUtc",
                table: "PayRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuditLog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", nullable: false),
                    EntityId = table.Column<int>(type: "INTEGER", nullable: true),
                    OldValue = table.Column<string>(type: "TEXT", nullable: true),
                    NewValue = table.Column<string>(type: "TEXT", nullable: true),
                    PerformedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ApplicationVersion = table.Column<string>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayRuns_Status_PayDate",
                table: "PayRuns",
                columns: new[] { "Status", "PayDate" });

            // Stamp existing runs with the engine that actually produced them. Their stubs were
            // computed with flat-percentage federal/state withholding and FICA charged on gross
            // (before the §125 exclusion). Recording that is what keeps preserved history
            // interpretable instead of merely old - without it, a future reader would assume
            // these amounts came from the Pub 15-T engine.
            migrationBuilder.Sql(@"
                UPDATE PayRuns
                SET CalculationEngineVersion = '1.0-legacy-flat-rate',
                    PostedAtUtc = PayDate
                WHERE CalculationEngineVersion IS NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_TimestampUtc",
                table: "AuditLog",
                column: "TimestampUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLog");

            migrationBuilder.DropIndex(
                name: "IX_PayRuns_Status_PayDate",
                table: "PayRuns");

            migrationBuilder.DropColumn(
                name: "CalculationEngineVersion",
                table: "PayRuns");

            migrationBuilder.DropColumn(
                name: "CalculationHash",
                table: "PayRuns");

            migrationBuilder.DropColumn(
                name: "PostedAtUtc",
                table: "PayRuns");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "PayRuns");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "PayRuns");

            migrationBuilder.DropColumn(
                name: "VoidedAtUtc",
                table: "PayRuns");
        }
    }
}
