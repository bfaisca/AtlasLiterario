using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AtlasLiterario.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCisleithaniaAndBohemiaMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "historical_country_mapping",
                columns: new[] { "id", "country_id", "note", "wikidata_qid" },
                values: new object[,]
                {
                    { 20, (short)40, "Cisleithania (Austrian half of Austria-Hungary)", "Q533534" },
                    { 21, (short)203, "Kingdom of Bohemia", "Q42585" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "historical_country_mapping",
                keyColumn: "id",
                keyValue: 20);

            migrationBuilder.DeleteData(
                table: "historical_country_mapping",
                keyColumn: "id",
                keyValue: 21);
        }
    }
}
