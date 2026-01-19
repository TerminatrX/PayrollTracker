using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PayrollManager.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddDeductionAndTaxLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add YtdTaxes column to PayStubs
            migrationBuilder.AddColumn<decimal>(
                name: "YtdTaxes",
                table: "PayStubs",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            // Create DeductionLines table
            migrationBuilder.CreateTable(
                name: "DeductionLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PayStubId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    IsPreTax = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeductionLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeductionLines_PayStubs_PayStubId",
                        column: x => x.PayStubId,
                        principalTable: "PayStubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Create TaxLines table
            migrationBuilder.CreateTable(
                name: "TaxLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PayStubId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Rate = table.Column<decimal>(type: "TEXT", nullable: false),
                    TaxableAmount = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxLines_PayStubs_PayStubId",
                        column: x => x.PayStubId,
                        principalTable: "PayStubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeductionLines_PayStubId",
                table: "DeductionLines",
                column: "PayStubId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxLines_PayStubId",
                table: "TaxLines",
                column: "PayStubId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaxLines");

            migrationBuilder.DropTable(
                name: "DeductionLines");

            migrationBuilder.DropColumn(
                name: "YtdTaxes",
                table: "PayStubs");
        }
    }
}
