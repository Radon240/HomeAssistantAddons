using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeAiAddon.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStateChangeEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StateChangeEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EntityId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    OldState = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    NewState = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    FriendlyName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    TimeFiredUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StateChangeEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StateChangeEvents_EntityId",
                table: "StateChangeEvents",
                column: "EntityId");

            migrationBuilder.CreateIndex(
                name: "IX_StateChangeEvents_ReceivedAtUtc",
                table: "StateChangeEvents",
                column: "ReceivedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StateChangeEvents");
        }
    }
}
