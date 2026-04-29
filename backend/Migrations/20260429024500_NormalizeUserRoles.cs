using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    public partial class NormalizeUserRoles : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE `Users`
                SET `Usertype` = CASE LOWER(TRIM(`Usertype`))
                    WHEN 'admin' THEN 'Admin'
                    WHEN 'organizer' THEN 'Organizer'
                    WHEN 'participant' THEN 'Participant'
                    WHEN 'volunteer' THEN 'Volunteer'
                    WHEN 'undefined' THEN 'Participant'
                    ELSE `Usertype`
                END
                WHERE `Usertype` IS NOT NULL;
                """
            );
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
