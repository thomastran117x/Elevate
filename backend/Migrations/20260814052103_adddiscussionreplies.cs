using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class adddiscussionreplies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClubDiscussionReplies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DiscussionId = table.Column<int>(type: "integer", nullable: false),
                    ParentReplyId = table.Column<int>(type: "integer", nullable: true),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubDiscussionReplies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClubDiscussionReplies_ClubDiscussionReplies_ParentReplyId",
                        column: x => x.ParentReplyId,
                        principalTable: "ClubDiscussionReplies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ClubDiscussionReplies_ClubDiscussions_DiscussionId",
                        column: x => x.DiscussionId,
                        principalTable: "ClubDiscussions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClubDiscussionReplies_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClubDiscussionReplyReactions",
                columns: table => new
                {
                    ReplyId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Reaction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClubDiscussionReplyReactions", x => new { x.ReplyId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ClubDiscussionReplyReactions_ClubDiscussionReplies_ReplyId",
                        column: x => x.ReplyId,
                        principalTable: "ClubDiscussionReplies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClubDiscussionReplyReactions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClubDiscussionReplies_DiscussionId_ParentReplyId_CreatedAt_~",
                table: "ClubDiscussionReplies",
                columns: new[] { "DiscussionId", "ParentReplyId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ClubDiscussionReplies_ParentReplyId",
                table: "ClubDiscussionReplies",
                column: "ParentReplyId");

            migrationBuilder.CreateIndex(
                name: "IX_ClubDiscussionReplies_UserId",
                table: "ClubDiscussionReplies",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClubDiscussionReplyReactions_UserId",
                table: "ClubDiscussionReplyReactions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClubDiscussionReplyReactions");

            migrationBuilder.DropTable(
                name: "ClubDiscussionReplies");
        }
    }
}
