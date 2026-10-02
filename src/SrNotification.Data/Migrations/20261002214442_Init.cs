using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SrNotification.Data.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RssFeeds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Title = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SiteUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    LastCheckedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSuccessAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    ETag = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RssFeeds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RssItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FeedId = table.Column<int>(type: "integer", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Title = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Link = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    Author = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsFromInitialFetch = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RssItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RssItems_RssFeeds_FeedId",
                        column: x => x.FeedId,
                        principalTable: "RssFeeds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RssFeeds_Url",
                table: "RssFeeds",
                column: "Url",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RssItems_FeedId_ExternalId",
                table: "RssItems",
                columns: new[] { "FeedId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RssItems_FetchedAt",
                table: "RssItems",
                column: "FetchedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RssItems");

            migrationBuilder.DropTable(
                name: "RssFeeds");
        }
    }
}
