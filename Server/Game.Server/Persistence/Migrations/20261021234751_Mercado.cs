using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Game.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Mercado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "market_listings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    SellerCharacterId = table.Column<int>(type: "integer", nullable: false),
                    SellerName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ItemUid = table.Column<long>(type: "bigint", nullable: false),
                    ItemId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Upgrade = table.Column<int>(type: "integer", nullable: false),
                    PityFails = table.Column<int>(type: "integer", nullable: false),
                    PityLevel = table.Column<int>(type: "integer", nullable: false),
                    RegainGuaranteed = table.Column<bool>(type: "boolean", nullable: false),
                    OptionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Price = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    BuyerName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SoldAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_listings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_market_listings_characters_SellerCharacterId",
                        column: x => x.SellerCharacterId,
                        principalTable: "characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_market_listings_SellerCharacterId",
                table: "market_listings",
                column: "SellerCharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_market_listings_State",
                table: "market_listings",
                column: "State");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "market_listings");
        }
    }
}
