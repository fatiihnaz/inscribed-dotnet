using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inscribed.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "clients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Locales = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    AllowAnonymousContentRead = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clients", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_clients_Key",
                table: "clients",
                column: "Key",
                unique: true);

            migrationBuilder.Sql(
                """
                DO $$ BEGIN
                  IF to_regclass('auth_clients') IS NOT NULL THEN
                    INSERT INTO clients ("Key", "Locales", "AllowAnonymousContentRead", "IsActive", "CreatedAt", "UpdatedAt")
                    SELECT "Key", "Locales", "AllowAnonymousContentRead", "IsActive", "CreatedAt", "UpdatedAt"
                    FROM auth_clients
                    ON CONFLICT ("Key") DO NOTHING;
                  END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "clients");
        }
    }
}
