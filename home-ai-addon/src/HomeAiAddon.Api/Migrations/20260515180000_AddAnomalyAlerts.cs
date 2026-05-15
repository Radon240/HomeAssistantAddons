using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeAiAddon.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAnomalyAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnomalyAlerts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DetectionId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EntityId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    AnomalyType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Score = table.Column<double>(type: "REAL", nullable: false),
                    Method = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Explanation = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    DetectedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PersistedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RelatedEventIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                    MetricsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnomalyAlerts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnomalyAlerts_DetectedAtUtc",
                table: "AnomalyAlerts",
                column: "DetectedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AnomalyAlerts_DetectionId",
                table: "AnomalyAlerts",
                column: "DetectionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnomalyAlerts_EntityId",
                table: "AnomalyAlerts",
                column: "EntityId");

            migrationBuilder.CreateIndex(
                name: "IX_AnomalyAlerts_Severity",
                table: "AnomalyAlerts",
                column: "Severity");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnomalyAlerts");
        }
    }
}
