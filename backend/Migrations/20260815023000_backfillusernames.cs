using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    [DbContext(typeof(AppDatabaseContext))]
    [Migration("20260815023000_backfillusernames")]
    public partial class BackfillUsernames : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    account RECORD;
                    base_username TEXT;
                    candidate TEXT;
                    suffix INTEGER;
                BEGIN
                    FOR account IN
                        SELECT "Id", "Email"
                        FROM "Users"
                        WHERE "Username" IS NULL OR btrim("Username") = ''
                        ORDER BY "Id"
                    LOOP
                        base_username := left(
                            COALESCE(
                                NULLIF(
                                    regexp_replace(split_part(account."Email", '@', 1), '[^a-zA-Z0-9._-]', '', 'g'),
                                    ''
                                ),
                                'user'
                            ),
                            35
                        );
                        candidate := base_username;
                        suffix := 0;

                        WHILE EXISTS (
                            SELECT 1
                            FROM "Users"
                            WHERE "Username" = candidate
                              AND "Id" <> account."Id"
                        ) LOOP
                            suffix := suffix + 1;
                            candidate := left(base_username, 35)
                                || '-'
                                || account."Id"::text
                                || CASE WHEN suffix = 1 THEN '' ELSE '-' || suffix::text END;
                        END LOOP;

                        UPDATE "Users"
                        SET "Username" = candidate,
                            "UpdatedAt" = NOW()
                        WHERE "Id" = account."Id";
                    END LOOP;
                END $$;
                """
            );
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Usernames may be changed after this migration, so a safe automatic rollback
            // cannot distinguish generated values from user-selected values.
        }
    }
}
