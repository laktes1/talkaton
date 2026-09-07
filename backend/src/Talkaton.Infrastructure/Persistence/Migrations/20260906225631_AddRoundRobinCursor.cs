using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Talkaton.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRoundRobinCursor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LastRoundRobinMemberId",
                table: "participant_lists",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastRoundRobinMemberId",
                table: "participant_lists");
        }
    }
}
