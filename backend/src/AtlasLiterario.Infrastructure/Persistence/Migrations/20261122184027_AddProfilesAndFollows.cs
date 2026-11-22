using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasLiterario.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfilesAndFollows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "handle",
                table: "user",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "normalized_name",
                table: "user",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "user_follow",
                columns: table => new
                {
                    follower_id = table.Column<Guid>(type: "uuid", nullable: false),
                    followed_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_follow", x => new { x.follower_id, x.followed_id });
                    table.CheckConstraint("ck_user_follow_not_self", "follower_id <> followed_id");
                    table.ForeignKey(
                        name: "fk_user_follow_user_followed_id",
                        column: x => x.followed_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_follow_user_follower_id",
                        column: x => x.follower_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_handle",
                table: "user",
                column: "handle",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_follow_followed_id",
                table: "user_follow",
                column: "followed_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_follow");

            migrationBuilder.DropIndex(
                name: "ix_user_handle",
                table: "user");

            migrationBuilder.DropColumn(
                name: "handle",
                table: "user");

            migrationBuilder.DropColumn(
                name: "normalized_name",
                table: "user");
        }
    }
}
