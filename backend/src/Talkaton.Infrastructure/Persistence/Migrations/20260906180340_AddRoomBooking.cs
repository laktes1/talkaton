using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Talkaton.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRoomBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RoomId",
                table: "events",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "rooms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Capacity = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rooms", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_events_RoomId_StartUtc",
                table: "events",
                columns: new[] { "RoomId", "StartUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_events_rooms_RoomId",
                table: "events",
                column: "RoomId",
                principalTable: "rooms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_events_rooms_RoomId",
                table: "events");

            migrationBuilder.DropTable(
                name: "rooms");

            migrationBuilder.DropIndex(
                name: "IX_events_RoomId_StartUtc",
                table: "events");

            migrationBuilder.DropColumn(
                name: "RoomId",
                table: "events");
        }
    }
}
