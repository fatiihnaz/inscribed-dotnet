using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inscribed.Auth.Issuer.Storage.Migrations
{
    /// <inheritdoc />
    public partial class SplitAdminCapabilities : Migration
    {
        private const string LegacyAdmin = "ARRAY['tenant:admin']";
        private const string SplitAdmin = "ARRAY['client:admin','service:admin']";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Rewrite("auth_memberships", """
                                 WHEN 'tenant:admin' THEN ARRAY['client:admin']
                """, LegacyAdmin));

            migrationBuilder.Sql(Rewrite("auth_service_keys", """
                                 WHEN 'tenant:admin' THEN ARRAY[]::text[]
                """, LegacyAdmin));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            const string toLegacy = """
                                 WHEN 'client:admin'  THEN ARRAY['tenant:admin']
                                 WHEN 'service:admin' THEN ARRAY['tenant:admin']
                """;

            migrationBuilder.Sql(Rewrite("auth_memberships", toLegacy, SplitAdmin));
            migrationBuilder.Sql(Rewrite("auth_service_keys", toLegacy, SplitAdmin));
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
