using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SrNotification.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationSender : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FannedOutAt",
                table: "RssItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                table: "NotificationDeliveries",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.CreateIndex(
                name: "IX_RssItems_NotFannedOut",
                table: "RssItems",
                column: "Id",
                filter: "\"FannedOutAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_Status_NextAttemptAt",
                table: "NotificationDeliveries",
                columns: new[] { "Status", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RssItems_NotFannedOut",
                table: "RssItems");

            migrationBuilder.DropIndex(
                name: "IX_NotificationDeliveries_Status_NextAttemptAt",
                table: "NotificationDeliveries");

            migrationBuilder.DropColumn(
                name: "FannedOutAt",
                table: "RssItems");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "NotificationDeliveries");
        }
    }
}
