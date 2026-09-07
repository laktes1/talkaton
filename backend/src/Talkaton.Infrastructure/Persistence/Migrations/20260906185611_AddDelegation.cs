using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Talkaton.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDelegation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "events",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "delegations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DelegateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delegations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_delegations_users_DelegateId",
                        column: x => x.DelegateId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_delegations_users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_events_CreatedByUserId",
                table: "events",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_delegations_DelegateId",
                table: "delegations",
                column: "DelegateId");

            migrationBuilder.CreateIndex(
                name: "IX_delegations_OwnerId_DelegateId",
                table: "delegations",
                columns: new[] { "OwnerId", "DelegateId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_events_users_CreatedByUserId",
                table: "events",
                column: "CreatedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_events_users_CreatedByUserId",
                table: "events");

            migrationBuilder.DropTable(
                name: "delegations");

            migrationBuilder.DropIndex(
                name: "IX_events_CreatedByUserId",
                table: "events");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "events");
        }
    }
}
