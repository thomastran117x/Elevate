using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class addpostcommentthreads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PostComments_PostId",
                table: "PostComments");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "PostComments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "PostComments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ParentCommentId",
                table: "PostComments",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PostCommentReactions",
                columns: table => new
                {
                    CommentId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Reaction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostCommentReactions", x => new { x.CommentId, x.UserId });
                    table.ForeignKey(
                        name: "FK_PostCommentReactions_PostComments_CommentId",
                        column: x => x.CommentId,
                        principalTable: "PostComments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PostCommentReactions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PostComments_ParentCommentId",
                table: "PostComments",
                column: "ParentCommentId");

            migrationBuilder.CreateIndex(
                name: "IX_PostComments_PostId_ParentCommentId_CreatedAt_Id",
                table: "PostComments",
                columns: new[] { "PostId", "ParentCommentId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PostCommentReactions_UserId",
                table: "PostCommentReactions",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_PostComments_PostComments_ParentCommentId",
                table: "PostComments",
                column: "ParentCommentId",
                principalTable: "PostComments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PostComments_PostComments_ParentCommentId",
                table: "PostComments");

            migrationBuilder.DropTable(
                name: "PostCommentReactions");

            migrationBuilder.DropIndex(
                name: "IX_PostComments_ParentCommentId",
                table: "PostComments");

            migrationBuilder.DropIndex(
                name: "IX_PostComments_PostId_ParentCommentId_CreatedAt_Id",
                table: "PostComments");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "PostComments");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "PostComments");

            migrationBuilder.DropColumn(
                name: "ParentCommentId",
                table: "PostComments");

            migrationBuilder.CreateIndex(
                name: "IX_PostComments_PostId",
                table: "PostComments",
                column: "PostId");
        }
    }
}
