using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Game.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "item_log",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CharacterId = table.Column<int>(type: "integer", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ItemId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    AlzDelta = table.Column<long>(type: "bigint", nullable: false),
                    Details = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_log", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "characters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AccountId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    NameKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ClassId = table.Column<int>(type: "integer", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    Xp = table.Column<int>(type: "integer", nullable: false),
                    Alz = table.Column<long>(type: "bigint", nullable: false),
                    PositionX = table.Column<float>(type: "real", nullable: false),
                    PositionZ = table.Column<float>(type: "real", nullable: false),
                    Hp = table.Column<int>(type: "integer", nullable: false),
                    Mp = table.Column<int>(type: "integer", nullable: false),
                    Sp = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SavedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_characters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_characters_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inventory_items",
                columns: table => new
                {
                    CharacterId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Rarity = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_items", x => new { x.CharacterId, x.ItemId });
                    table.ForeignKey(
                        name: "FK_inventory_items_characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounts_Name",
                table: "accounts",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_characters_AccountId",
                table: "characters",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_characters_NameKey",
                table: "characters",
                column: "NameKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_item_log_CharacterId_At",
                table: "item_log",
                columns: new[] { "CharacterId", "At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_items");

            migrationBuilder.DropTable(
                name: "item_log");

            migrationBuilder.DropTable(
                name: "characters");

            migrationBuilder.DropTable(
                name: "accounts");
        }
    }
}
