using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ordering.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderFulfilmentRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "GracePeriodEndsAt",
                schema: "ordering",
                table: "orders",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "StockShortfallPolicy",
                schema: "ordering",
                table: "orders",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "AllOrNothing");

            migrationBuilder.AddColumn<bool>(
                name: "IsUnfulfillable",
                schema: "ordering",
                table: "orderItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "OrderDiscountShare",
                schema: "ordering",
                table: "orderItems",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PromotionLabel",
                schema: "ordering",
                table: "orderItems",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE ordering.orders SET "GracePeriodEndsAt" = "OrderDate" + interval '1 minute';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GracePeriodEndsAt",
                schema: "ordering",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "StockShortfallPolicy",
                schema: "ordering",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "IsUnfulfillable",
                schema: "ordering",
                table: "orderItems");

            migrationBuilder.DropColumn(
                name: "OrderDiscountShare",
                schema: "ordering",
                table: "orderItems");

            migrationBuilder.DropColumn(
                name: "PromotionLabel",
                schema: "ordering",
                table: "orderItems");
        }
    }
}
