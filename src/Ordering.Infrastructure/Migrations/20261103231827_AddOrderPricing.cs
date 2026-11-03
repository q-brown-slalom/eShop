using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ordering.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DiscountTotal",
                schema: "ordering",
                table: "orders",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<long>(
                name: "PricingSnapshotVersion",
                schema: "ordering",
                table: "orders",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<decimal>(
                name: "ShippingFee",
                schema: "ordering",
                table: "orders",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Subtotal",
                schema: "ordering",
                table: "orders",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Total",
                schema: "ordering",
                table: "orders",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BasePrice",
                schema: "ordering",
                table: "orderItems",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                UPDATE ordering."orderItems" SET "BasePrice" = "UnitPrice";

                UPDATE ordering.orders AS o
                SET "Subtotal" = t.subtotal,
                    "DiscountTotal" = t.discount_total,
                    "ShippingFee" = 0,
                    "Total" = t.subtotal - t.discount_total,
                    "PricingSnapshotVersion" = 0
                FROM (
                    SELECT ord."Id" AS order_id,
                           COALESCE(SUM(oi."Units" * oi."UnitPrice"), 0) AS subtotal,
                           COALESCE(SUM(oi."Discount"), 0) AS discount_total
                    FROM ordering.orders AS ord
                    LEFT JOIN ordering."orderItems" AS oi ON oi."OrderId" = ord."Id"
                    GROUP BY ord."Id"
                ) AS t
                WHERE o."Id" = t.order_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscountTotal",
                schema: "ordering",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "PricingSnapshotVersion",
                schema: "ordering",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ShippingFee",
                schema: "ordering",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "Subtotal",
                schema: "ordering",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "Total",
                schema: "ordering",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "BasePrice",
                schema: "ordering",
                table: "orderItems");
        }
    }
}
