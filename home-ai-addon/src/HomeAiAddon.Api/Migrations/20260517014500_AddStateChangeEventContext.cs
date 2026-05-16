using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeAiAddon.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStateChangeEventContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContextId",
                table: "StateChangeEvents",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContextParentId",
                table: "StateChangeEvents",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContextUserId",
                table: "StateChangeEvents",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StateChangeEvents_ContextParentId",
                table: "StateChangeEvents",
                column: "ContextParentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StateChangeEvents_ContextParentId",
                table: "StateChangeEvents");

            migrationBuilder.DropColumn(
                name: "ContextId",
                table: "StateChangeEvents");

            migrationBuilder.DropColumn(
                name: "ContextParentId",
                table: "StateChangeEvents");

            migrationBuilder.DropColumn(
                name: "ContextUserId",
                table: "StateChangeEvents");
        }
    }
}
