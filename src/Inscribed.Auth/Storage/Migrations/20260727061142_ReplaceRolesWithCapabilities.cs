using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inscribed.Auth.Storage.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceRolesWithCapabilities : Migration
    {
        private const string LegacyRoles = "ARRAY['cms:access','cms:read','cms:admin']";
        private const string Capabilities = "ARRAY['content:read','content:write','schema:sync','tenant:admin']";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Rewrite("auth_memberships", """
                                 WHEN 'cms:access' THEN ARRAY['content:read','content:write']
                                 WHEN 'cms:read'   THEN ARRAY['content:read']
                                 WHEN 'cms:admin'  THEN ARRAY['tenant:admin']
                """, LegacyRoles));

            migrationBuilder.Sql(Rewrite("auth_service_keys", """
                                 WHEN 'cms:access' THEN ARRAY['content:read','content:write','schema:sync']
                                 WHEN 'cms:read'   THEN ARRAY['content:read']
                                 WHEN 'cms:admin'  THEN ARRAY[]::text[]
                """, LegacyRoles));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            const string toLegacy = """
                                 WHEN 'content:read'  THEN ARRAY['cms:read']
                                 WHEN 'content:write' THEN ARRAY['cms:access']
                                 WHEN 'schema:sync'   THEN ARRAY['cms:access']
                                 WHEN 'tenant:admin'  THEN ARRAY['cms:admin']
                """;

            migrationBuilder.Sql(Rewrite("auth_memberships", toLegacy, Capabilities));
            migrationBuilder.Sql(Rewrite("auth_service_keys", toLegacy, Capabilities));
        }

        private static string Rewrite(string table, string cases, string matching) => $"""
            UPDATE {table} AS t
            SET "Roles" = COALESCE((
                    SELECT array_agg(DISTINCT mapped ORDER BY mapped)
                    FROM unnest(t."Roles") AS source,
                         unnest(CASE source
            {cases}
                             ELSE ARRAY[source]
                         END) AS mapped
                ), ARRAY[]::text[]),
                "UpdatedAt" = now() at time zone 'utc',
                "Version" = t."Version" + 1
            WHERE t."Roles" && {matching};
            """;
    }
}
