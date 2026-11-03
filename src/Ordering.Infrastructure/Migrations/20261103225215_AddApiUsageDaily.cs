using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ordering.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApiUsageDaily : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "apiusagedaily",
                schema: "ordering",
                columns: table => new
                {
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Operation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Calls = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_apiusagedaily", x => new { x.Day, x.Operation });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "apiusagedaily",
                schema: "ordering");
        }
    }
}
