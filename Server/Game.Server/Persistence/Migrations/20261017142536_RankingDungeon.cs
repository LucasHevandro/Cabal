using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Game.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RankingDungeon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dungeon_runs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DungeonId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Seconds = table.Column<double>(type: "double precision", nullable: false),
                    Medal = table.Column<int>(type: "integer", nullable: false),
                    MaxCombo = table.Column<int>(type: "integer", nullable: false),
                    Members = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dungeon_runs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_dungeon_runs_DungeonId_Seconds",
                table: "dungeon_runs",
                columns: new[] { "DungeonId", "Seconds" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dungeon_runs");
        }
    }
}
