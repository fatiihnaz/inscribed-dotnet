using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inscribed.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLocale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_content_blocks_ClientId_Slug",
                table: "content_blocks");

            migrationBuilder.DropIndex(
                name: "IX_content_blocks_ClientId_Slug_BlockPath",
                table: "content_blocks");

            migrationBuilder.AddColumn<string>(
                name: "Locale",
                table: "content_blocks",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Locale",
                table: "collection_items",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TranslationGroupId",
                table: "collection_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                @"UPDATE collection_items SET ""TranslationGroupId"" = gen_random_uuid() WHERE ""TranslationGroupId"" IS NULL;");

            migrationBuilder.AlterColumn<Guid>(
                name: "TranslationGroupId",
                table: "collection_items",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_content_blocks_ClientId_Locale_Slug",
                table: "content_blocks",
                columns: new[] { "ClientId", "Locale", "Slug" });

            migrationBuilder.CreateIndex(
                name: "IX_content_blocks_ClientId_Locale_Slug_BlockPath",
                table: "content_blocks",
                columns: new[] { "ClientId", "Locale", "Slug", "BlockPath" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_collection_items_CollectionKey_Locale",
                table: "collection_items",
                columns: new[] { "CollectionKey", "Locale" });

            migrationBuilder.CreateIndex(
                name: "IX_collection_items_TranslationGroupId_Locale",
                table: "collection_items",
                columns: new[] { "TranslationGroupId", "Locale" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_content_blocks_ClientId_Locale_Slug",
                table: "content_blocks");

            migrationBuilder.DropIndex(
                name: "IX_content_blocks_ClientId_Locale_Slug_BlockPath",
                table: "content_blocks");

            migrationBuilder.DropIndex(
                name: "IX_collection_items_CollectionKey_Locale",
                table: "collection_items");

            migrationBuilder.DropIndex(
                name: "IX_collection_items_TranslationGroupId_Locale",
                table: "collection_items");

            migrationBuilder.DropColumn(
                name: "Locale",
                table: "content_blocks");

            migrationBuilder.DropColumn(
                name: "Locale",
                table: "collection_items");

            migrationBuilder.DropColumn(
                name: "TranslationGroupId",
                table: "collection_items");

            migrationBuilder.CreateIndex(
                name: "IX_content_blocks_ClientId_Slug",
                table: "content_blocks",
                columns: new[] { "ClientId", "Slug" });

            migrationBuilder.CreateIndex(
                name: "IX_content_blocks_ClientId_Slug_BlockPath",
                table: "content_blocks",
                columns: new[] { "ClientId", "Slug", "BlockPath" },
                unique: true);
        }
    }
}
