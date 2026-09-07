using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Talkaton.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMeetingBuffer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BufferAfterMinutes",
                table: "users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BufferBeforeMinutes",
                table: "users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BufferAfterMinutes",
                table: "users");

            migrationBuilder.DropColumn(
                name: "BufferBeforeMinutes",
                table: "users");
        }
    }
}
