using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inscribed.Auth.Issuer.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddClientLocales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "Locales",
                table: "auth_clients",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Locales",
                table: "auth_clients");
        }
    }
}
