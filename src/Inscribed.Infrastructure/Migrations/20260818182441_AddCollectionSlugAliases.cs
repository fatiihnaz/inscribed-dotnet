using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inscribed.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectionSlugAliases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "collection_slug_aliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    CollectionKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Slug = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_slug_aliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_collection_slug_aliases_collection_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "collection_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_collection_slug_aliases_CollectionKey_Slug",
                table: "collection_slug_aliases",
                columns: new[] { "CollectionKey", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_collection_slug_aliases_ItemId",
                table: "collection_slug_aliases",
                column: "ItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collection_slug_aliases");
        }
    }
}
