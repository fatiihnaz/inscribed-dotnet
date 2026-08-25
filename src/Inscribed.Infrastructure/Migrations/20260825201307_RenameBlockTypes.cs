using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inscribed.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameBlockTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE content_blocks SET "BlockType" = 'ObjectArray' WHERE "BlockType" = 'List';""");
            migrationBuilder.Sql("""UPDATE content_blocks SET "BlockType" = 'ShortText' WHERE "BlockType" = 'Text';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE content_blocks SET "BlockType" = 'List' WHERE "BlockType" = 'ObjectArray';""");
        }
    }
}
