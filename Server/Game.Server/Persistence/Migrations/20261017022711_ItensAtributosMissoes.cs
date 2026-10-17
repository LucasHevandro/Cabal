using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Game.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ItensAtributosMissoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AwakeningGauge",
                table: "characters",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Dexterity",
                table: "characters",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Intelligence",
                table: "characters",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Strength",
                table: "characters",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "character_quests",
                columns: table => new
                {
                    CharacterId = table.Column<int>(type: "integer", nullable: false),
                    QuestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Completed = table.Column<bool>(type: "boolean", nullable: false),
                    Counts = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character_quests", x => new { x.CharacterId, x.QuestId });
                    table.ForeignKey(
                        name: "FK_character_quests_characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "item_instances",
                columns: table => new
                {
                    Uid = table.Column<long>(type: "bigint", nullable: false),
                    CharacterId = table.Column<int>(type: "integer", nullable: false),
                    Container = table.Column<int>(type: "integer", nullable: false),
                    Slot = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Upgrade = table.Column<int>(type: "integer", nullable: false),
                    PityFails = table.Column<int>(type: "integer", nullable: false),
                    PityLevel = table.Column<int>(type: "integer", nullable: false),
                    RegainGuaranteed = table.Column<bool>(type: "boolean", nullable: false),
                    OptionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_instances", x => x.Uid);
                    table.ForeignKey(
                        name: "FK_item_instances_characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_item_instances_CharacterId",
                table: "item_instances",
                column: "CharacterId");

            // Converte o inventário antigo (uma linha por tipo de item) em itens com id único, um por posição da bolsa.
            migrationBuilder.Sql("""
                INSERT INTO item_instances ("Uid", "CharacterId", "Container", "Slot", "ItemId", "Quantity",
                    "Upgrade", "PityFails", "PityLevel", "RegainGuaranteed", "OptionId")
                SELECT row_number() OVER (ORDER BY "CharacterId", "ItemId"), "CharacterId", 0,
                    (row_number() OVER (PARTITION BY "CharacterId" ORDER BY "ItemId")) - 1, "ItemId", "Quantity", 0, 0, 0, false, ''
                FROM inventory_items WHERE "Quantity" > 0;
                """);

            // Personagens que já existiam ganham a arma inicial vestida e os atributos iniciais da classe (1 = Arcanista).
            migrationBuilder.Sql("""
                INSERT INTO item_instances ("Uid", "CharacterId", "Container", "Slot", "ItemId", "Quantity",
                    "Upgrade", "PityFails", "PityLevel", "RegainGuaranteed", "OptionId")
                SELECT (SELECT COALESCE(MAX("Uid"), 0) FROM item_instances) + row_number() OVER (ORDER BY "Id"), "Id", 1, 0,
                    CASE WHEN "ClassId" = 1 THEN 'cajado_ferro' ELSE 'espada_ferro' END, 1, 0, 0, 0, false, ''
                FROM characters;
                UPDATE characters SET
                    "Strength" = CASE WHEN "ClassId" = 1 THEN 3 ELSE 10 END,
                    "Dexterity" = CASE WHEN "ClassId" = 1 THEN 5 ELSE 6 END,
                    "Intelligence" = CASE WHEN "ClassId" = 1 THEN 10 ELSE 2 END;
                """);

            migrationBuilder.DropTable(
                name: "inventory_items");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "character_quests");

            migrationBuilder.DropTable(
                name: "item_instances");

            migrationBuilder.DropColumn(
                name: "AwakeningGauge",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "Dexterity",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "Intelligence",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "Strength",
                table: "characters");

            migrationBuilder.CreateTable(
                name: "inventory_items",
                columns: table => new
                {
                    CharacterId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Rarity = table.Column<int>(type: "integer", nullable: false)
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
        }
    }
}
