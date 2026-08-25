using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inscribed.Auth.Issuer.Storage.Migrations
{
    /// <inheritdoc />
    public partial class RemoveClientCmsFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowAnonymousContentRead",
                table: "auth_clients");

            migrationBuilder.DropColumn(
                name: "Locales",
                table: "auth_clients");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowAnonymousContentRead",
                table: "auth_clients",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string[]>(
                name: "Locales",
                table: "auth_clients",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");
        }
    }
}
