using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AtlasLiterario.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "author",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    birth_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_author", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "book",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    subtitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    isbn13 = table.Column<string>(type: "character(13)", fixedLength: true, maxLength: 13, nullable: true),
                    isbn10 = table.Column<string>(type: "character(10)", fixedLength: true, maxLength: 10, nullable: true),
                    publisher = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    publication_year = table.Column<short>(type: "smallint", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    cover_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    original_language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    genres = table.Column<List<string>>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    geo_enrichment_status = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_book", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "country",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_pt = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    iso_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    iso_code2 = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    continent = table.Column<short>(type: "smallint", nullable: false),
                    geo_json_id = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    wikidata_qid = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_country", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "external_lookup_cache",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_lookup_cache", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "user",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    map_visibility = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "external_author_identifier",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<short>(type: "smallint", nullable: false),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_author_identifier", x => x.id);
                    table.ForeignKey(
                        name: "fk_external_author_identifier_author_author_id",
                        column: x => x.author_id,
                        principalTable: "author",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "book_author",
                columns: table => new
                {
                    book_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_book_author", x => new { x.book_id, x.author_id });
                    table.ForeignKey(
                        name: "fk_book_author_author_author_id",
                        column: x => x.author_id,
                        principalTable: "author",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_book_author_book_book_id",
                        column: x => x.book_id,
                        principalTable: "book",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "external_book_identifier",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    book_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<short>(type: "smallint", nullable: false),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_book_identifier", x => x.id);
                    table.ForeignKey(
                        name: "fk_external_book_identifier_book_book_id",
                        column: x => x.book_id,
                        principalTable: "book",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "author_country",
                columns: table => new
                {
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    country_id = table.Column<short>(type: "smallint", nullable: false),
                    relationship_type = table.Column<short>(type: "smallint", nullable: false),
                    source = table.Column<short>(type: "smallint", nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: false),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_author_country", x => new { x.author_id, x.country_id, x.relationship_type });
                    table.CheckConstraint("ck_author_country_confidence", "confidence >= 0 AND confidence <= 1");
                    table.ForeignKey(
                        name: "fk_author_country_author_author_id",
                        column: x => x.author_id,
                        principalTable: "author",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_author_country_country_country_id",
                        column: x => x.country_id,
                        principalTable: "country",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "book_origin_country",
                columns: table => new
                {
                    book_id = table.Column<Guid>(type: "uuid", nullable: false),
                    country_id = table.Column<short>(type: "smallint", nullable: false),
                    source = table.Column<short>(type: "smallint", nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: false),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_book_origin_country", x => new { x.book_id, x.country_id });
                    table.CheckConstraint("ck_book_origin_country_confidence", "confidence >= 0 AND confidence <= 1");
                    table.ForeignKey(
                        name: "fk_book_origin_country_book_book_id",
                        column: x => x.book_id,
                        principalTable: "book",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_book_origin_country_country_country_id",
                        column: x => x.country_id,
                        principalTable: "country",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "historical_country_mapping",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    wikidata_qid = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    country_id = table.Column<short>(type: "smallint", nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_historical_country_mapping", x => x.id);
                    table.ForeignKey(
                        name: "fk_historical_country_mapping_country_country_id",
                        column: x => x.country_id,
                        principalTable: "country",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "map_share",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    map_type = table.Column<short>(type: "smallint", nullable: false),
                    token = table.Column<string>(type: "character varying(43)", maxLength: 43, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_map_share", x => x.id);
                    table.ForeignKey(
                        name: "fk_map_share_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "password_reset_token",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_password_reset_token", x => x.id);
                    table.ForeignKey(
                        name: "fk_password_reset_token_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_token",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replaced_by_token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_token", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_token_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_book",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    book_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    rating = table.Column<short>(type: "smallint", nullable: true),
                    review = table.Column<string>(type: "text", nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    finished_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_book", x => x.id);
                    table.CheckConstraint("ck_user_book_dates", "start_date IS NULL OR finished_date IS NULL OR start_date <= finished_date");
                    table.CheckConstraint("ck_user_book_rating", "rating IS NULL OR (rating >= 1 AND rating <= 5)");
                    table.ForeignKey(
                        name: "fk_user_book_book_book_id",
                        column: x => x.book_id,
                        principalTable: "book",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_book_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "country",
                columns: new[] { "id", "continent", "geo_json_id", "iso_code", "iso_code2", "name", "name_pt", "wikidata_qid" },
                values: new object[,]
                {
                    { (short)4, (short)1, "AFG", "AFG", "AF", "Afghanistan", "Afeganistão", "Q889" },
                    { (short)8, (short)2, "ALB", "ALB", "AL", "Albania", "Albânia", "Q222" },
                    { (short)10, (short)6, "ATA", "ATA", "AQ", "Antarctica", "Antártida", null },
                    { (short)12, (short)0, "DZA", "DZA", "DZ", "Algeria", "Argélia", "Q262" },
                    { (short)16, (short)5, "ASM", "ASM", "AS", "American Samoa", "Samoa Americana", null },
                    { (short)20, (short)2, "AND", "AND", "AD", "Andorra", "Andorra", "Q228" },
                    { (short)24, (short)0, "AGO", "AGO", "AO", "Angola", "Angola", "Q916" },
                    { (short)28, (short)3, "ATG", "ATG", "AG", "Antigua and Barbuda", "Antígua e Barbuda", "Q781" },
                    { (short)31, (short)1, "AZE", "AZE", "AZ", "Azerbaijan", "Azerbaijão", "Q227" },
                    { (short)32, (short)4, "ARG", "ARG", "AR", "Argentina", "Argentina", "Q414" },
                    { (short)36, (short)5, "AUS", "AUS", "AU", "Australia", "Austrália", "Q408" },
                    { (short)40, (short)2, "AUT", "AUT", "AT", "Austria", "Áustria", "Q40" },
                    { (short)44, (short)3, "BHS", "BHS", "BS", "Bahamas", "Bahamas", "Q778" },
                    { (short)48, (short)1, "BHR", "BHR", "BH", "Bahrain", "Bahrein", "Q398" },
                    { (short)50, (short)1, "BGD", "BGD", "BD", "Bangladesh", "Bangladesh", "Q902" },
                    { (short)51, (short)1, "ARM", "ARM", "AM", "Armenia", "Armênia", "Q399" },
                    { (short)52, (short)3, "BRB", "BRB", "BB", "Barbados", "Barbados", "Q244" },
                    { (short)56, (short)2, "BEL", "BEL", "BE", "Belgium", "Bélgica", "Q31" },
                    { (short)60, (short)3, "BMU", "BMU", "BM", "Bermuda", "Bermudas", null },
                    { (short)64, (short)1, "BTN", "BTN", "BT", "Bhutan", "Butão", "Q917" },
                    { (short)68, (short)4, "BOL", "BOL", "BO", "Bolivia", "Bolívia", "Q750" },
                    { (short)70, (short)2, "BIH", "BIH", "BA", "Bosnia and Herzegovina", "Bósnia e Herzegovina", "Q225" },
                    { (short)72, (short)0, "BWA", "BWA", "BW", "Botswana", "Botsuana", "Q963" },
                    { (short)74, (short)6, "BVT", "BVT", "BV", "Bouvet Island", "Ilha Bouvet", null },
                    { (short)76, (short)4, "BRA", "BRA", "BR", "Brazil", "Brasil", "Q155" },
                    { (short)84, (short)3, "BLZ", "BLZ", "BZ", "Belize", "Belize", "Q242" },
                    { (short)86, (short)1, "IOT", "IOT", "IO", "British Indian Ocean Territory", "Território Britânico do Oceano Índico", null },
                    { (short)90, (short)5, "SLB", "SLB", "SB", "Solomon Islands", "Ilhas Salomão", "Q685" },
                    { (short)92, (short)3, "VGB", "VGB", "VG", "Virgin Islands (British)", "Ilhas Virgens Britânicas", null },
                    { (short)96, (short)1, "BRN", "BRN", "BN", "Brunei Darussalam", "Brunei", "Q921" },
                    { (short)100, (short)2, "BGR", "BGR", "BG", "Bulgaria", "Bulgária", "Q219" },
                    { (short)104, (short)1, "MMR", "MMR", "MM", "Myanmar", "Mianmar", "Q836" },
                    { (short)108, (short)0, "BDI", "BDI", "BI", "Burundi", "Burundi", "Q967" },
                    { (short)112, (short)2, "BLR", "BLR", "BY", "Belarus", "Bielorrússia", "Q184" },
                    { (short)116, (short)1, "KHM", "KHM", "KH", "Cambodia", "Camboja", "Q424" },
                    { (short)120, (short)0, "CMR", "CMR", "CM", "Cameroon", "Camarões", "Q1009" },
                    { (short)124, (short)3, "CAN", "CAN", "CA", "Canada", "Canadá", "Q16" },
                    { (short)132, (short)0, "CPV", "CPV", "CV", "Cabo Verde", "Cabo Verde", "Q1011" },
                    { (short)136, (short)3, "CYM", "CYM", "KY", "Cayman Islands", "Ilhas Cayman", null },
                    { (short)140, (short)0, "CAF", "CAF", "CF", "Central African Republic", "República Centro-Africana", "Q929" },
                    { (short)144, (short)1, "LKA", "LKA", "LK", "Sri Lanka", "Sri Lanka", "Q854" },
                    { (short)148, (short)0, "TCD", "TCD", "TD", "Chad", "Chade", "Q657" },
                    { (short)152, (short)4, "CHL", "CHL", "CL", "Chile", "Chile", "Q298" },
                    { (short)156, (short)1, "CHN", "CHN", "CN", "China", "China", "Q148" },
                    { (short)158, (short)1, "TWN", "TWN", "TW", "Taiwan", "Taiwan", "Q865" },
                    { (short)162, (short)5, "CXR", "CXR", "CX", "Christmas Island", "Ilha Christmas", null },
                    { (short)166, (short)5, "CCK", "CCK", "CC", "Cocos (Keeling) Islands", "Ilhas Cocos (Keeling)", null },
                    { (short)170, (short)4, "COL", "COL", "CO", "Colombia", "Colômbia", "Q739" },
                    { (short)174, (short)0, "COM", "COM", "KM", "Comoros", "Comores", "Q970" },
                    { (short)175, (short)0, "MYT", "MYT", "YT", "Mayotte", "Mayotte", null },
                    { (short)178, (short)0, "COG", "COG", "CG", "Congo", "República do Congo", "Q971" },
                    { (short)180, (short)0, "COD", "COD", "CD", "Congo, Democratic Republic of the", "República Democrática do Congo", "Q974" },
                    { (short)184, (short)5, "COK", "COK", "CK", "Cook Islands", "Ilhas Cook", null },
                    { (short)188, (short)3, "CRI", "CRI", "CR", "Costa Rica", "Costa Rica", "Q800" },
                    { (short)191, (short)2, "HRV", "HRV", "HR", "Croatia", "Croácia", "Q224" },
                    { (short)192, (short)3, "CUB", "CUB", "CU", "Cuba", "Cuba", "Q241" },
                    { (short)196, (short)2, "CYP", "CYP", "CY", "Cyprus", "Chipre", "Q229" },
                    { (short)203, (short)2, "CZE", "CZE", "CZ", "Czechia", "Tchéquia", "Q213" },
                    { (short)204, (short)0, "BEN", "BEN", "BJ", "Benin", "Benin", "Q962" },
                    { (short)208, (short)2, "DNK", "DNK", "DK", "Denmark", "Dinamarca", "Q35" },
                    { (short)212, (short)3, "DMA", "DMA", "DM", "Dominica", "Dominica", "Q784" },
                    { (short)214, (short)3, "DOM", "DOM", "DO", "Dominican Republic", "República Dominicana", "Q786" },
                    { (short)218, (short)4, "ECU", "ECU", "EC", "Ecuador", "Equador", "Q736" },
                    { (short)222, (short)3, "SLV", "SLV", "SV", "El Salvador", "El Salvador", "Q792" },
                    { (short)226, (short)0, "GNQ", "GNQ", "GQ", "Equatorial Guinea", "Guiné Equatorial", "Q983" },
                    { (short)231, (short)0, "ETH", "ETH", "ET", "Ethiopia", "Etiópia", "Q115" },
                    { (short)232, (short)0, "ERI", "ERI", "ER", "Eritrea", "Eritreia", "Q986" },
                    { (short)233, (short)2, "EST", "EST", "EE", "Estonia", "Estônia", "Q191" },
                    { (short)234, (short)2, "FRO", "FRO", "FO", "Faroe Islands", "Ilhas Feroé", null },
                    { (short)238, (short)4, "FLK", "FLK", "FK", "Falkland Islands (Malvinas)", "Ilhas Malvinas", null },
                    { (short)239, (short)6, "SGS", "SGS", "GS", "South Georgia and the South Sandwich Islands", "Ilhas Geórgia do Sul e Sandwich do Sul", null },
                    { (short)242, (short)5, "FJI", "FJI", "FJ", "Fiji", "Fiji", "Q712" },
                    { (short)246, (short)2, "FIN", "FIN", "FI", "Finland", "Finlândia", "Q33" },
                    { (short)248, (short)2, "ALA", "ALA", "AX", "Åland Islands", "Ilhas Åland", null },
                    { (short)250, (short)2, "FRA", "FRA", "FR", "France", "França", "Q142" },
                    { (short)254, (short)4, "GUF", "GUF", "GF", "French Guiana", "Guiana Francesa", null },
                    { (short)258, (short)5, "PYF", "PYF", "PF", "French Polynesia", "Polinésia Francesa", null },
                    { (short)260, (short)6, "ATF", "ATF", "TF", "French Southern Territories", "Terras Austrais e Antárticas Francesas", null },
                    { (short)262, (short)0, "DJI", "DJI", "DJ", "Djibouti", "Djibuti", "Q977" },
                    { (short)266, (short)0, "GAB", "GAB", "GA", "Gabon", "Gabão", "Q1000" },
                    { (short)268, (short)1, "GEO", "GEO", "GE", "Georgia", "Geórgia", "Q230" },
                    { (short)270, (short)0, "GMB", "GMB", "GM", "Gambia", "Gâmbia", "Q1005" },
                    { (short)275, (short)1, "PSE", "PSE", "PS", "Palestine, State of", "Palestina", "Q219060" },
                    { (short)276, (short)2, "DEU", "DEU", "DE", "Germany", "Alemanha", "Q183" },
                    { (short)288, (short)0, "GHA", "GHA", "GH", "Ghana", "Gana", "Q117" },
                    { (short)292, (short)2, "GIB", "GIB", "GI", "Gibraltar", "Gibraltar", null },
                    { (short)296, (short)5, "KIR", "KIR", "KI", "Kiribati", "Kiribati", "Q710" },
                    { (short)300, (short)2, "GRC", "GRC", "GR", "Greece", "Grécia", "Q41" },
                    { (short)304, (short)3, "GRL", "GRL", "GL", "Greenland", "Groenlândia", "Q223" },
                    { (short)308, (short)3, "GRD", "GRD", "GD", "Grenada", "Granada", "Q769" },
                    { (short)312, (short)3, "GLP", "GLP", "GP", "Guadeloupe", "Guadalupe", null },
                    { (short)316, (short)5, "GUM", "GUM", "GU", "Guam", "Guam", null },
                    { (short)320, (short)3, "GTM", "GTM", "GT", "Guatemala", "Guatemala", "Q774" },
                    { (short)324, (short)0, "GIN", "GIN", "GN", "Guinea", "Guiné", "Q1006" },
                    { (short)328, (short)4, "GUY", "GUY", "GY", "Guyana", "Guiana", "Q734" },
                    { (short)332, (short)3, "HTI", "HTI", "HT", "Haiti", "Haiti", "Q790" },
                    { (short)334, (short)6, "HMD", "HMD", "HM", "Heard Island and McDonald Islands", "Ilha Heard e Ilhas McDonald", null },
                    { (short)336, (short)2, "VAT", "VAT", "VA", "Holy See", "Vaticano", "Q237" },
                    { (short)340, (short)3, "HND", "HND", "HN", "Honduras", "Honduras", "Q783" },
                    { (short)344, (short)1, "HKG", "HKG", "HK", "Hong Kong", "Hong Kong", "Q8646" },
                    { (short)348, (short)2, "HUN", "HUN", "HU", "Hungary", "Hungria", "Q28" },
                    { (short)352, (short)2, "ISL", "ISL", "IS", "Iceland", "Islândia", "Q189" },
                    { (short)356, (short)1, "IND", "IND", "IN", "India", "Índia", "Q668" },
                    { (short)360, (short)1, "IDN", "IDN", "ID", "Indonesia", "Indonésia", "Q252" },
                    { (short)364, (short)1, "IRN", "IRN", "IR", "Iran", "Irã", "Q794" },
                    { (short)368, (short)1, "IRQ", "IRQ", "IQ", "Iraq", "Iraque", "Q796" },
                    { (short)372, (short)2, "IRL", "IRL", "IE", "Ireland", "Irlanda", "Q27" },
                    { (short)376, (short)1, "ISR", "ISR", "IL", "Israel", "Israel", "Q801" },
                    { (short)380, (short)2, "ITA", "ITA", "IT", "Italy", "Itália", "Q38" },
                    { (short)384, (short)0, "CIV", "CIV", "CI", "Côte d'Ivoire", "Costa do Marfim", "Q1008" },
                    { (short)388, (short)3, "JAM", "JAM", "JM", "Jamaica", "Jamaica", "Q766" },
                    { (short)392, (short)1, "JPN", "JPN", "JP", "Japan", "Japão", "Q17" },
                    { (short)398, (short)1, "KAZ", "KAZ", "KZ", "Kazakhstan", "Cazaquistão", "Q232" },
                    { (short)400, (short)1, "JOR", "JOR", "JO", "Jordan", "Jordânia", "Q810" },
                    { (short)404, (short)0, "KEN", "KEN", "KE", "Kenya", "Quênia", "Q114" },
                    { (short)408, (short)1, "PRK", "PRK", "KP", "North Korea", "Coreia do Norte", "Q423" },
                    { (short)410, (short)1, "KOR", "KOR", "KR", "South Korea", "Coreia do Sul", "Q884" },
                    { (short)414, (short)1, "KWT", "KWT", "KW", "Kuwait", "Kuwait", "Q817" },
                    { (short)417, (short)1, "KGZ", "KGZ", "KG", "Kyrgyzstan", "Quirguistão", "Q813" },
                    { (short)418, (short)1, "LAO", "LAO", "LA", "Laos", "Laos", "Q819" },
                    { (short)422, (short)1, "LBN", "LBN", "LB", "Lebanon", "Líbano", "Q822" },
                    { (short)426, (short)0, "LSO", "LSO", "LS", "Lesotho", "Lesoto", "Q1013" },
                    { (short)428, (short)2, "LVA", "LVA", "LV", "Latvia", "Letônia", "Q211" },
                    { (short)430, (short)0, "LBR", "LBR", "LR", "Liberia", "Libéria", "Q1014" },
                    { (short)434, (short)0, "LBY", "LBY", "LY", "Libya", "Líbia", "Q1016" },
                    { (short)438, (short)2, "LIE", "LIE", "LI", "Liechtenstein", "Liechtenstein", "Q347" },
                    { (short)440, (short)2, "LTU", "LTU", "LT", "Lithuania", "Lituânia", "Q37" },
                    { (short)442, (short)2, "LUX", "LUX", "LU", "Luxembourg", "Luxemburgo", "Q32" },
                    { (short)446, (short)1, "MAC", "MAC", "MO", "Macao", "Macau", "Q14773" },
                    { (short)450, (short)0, "MDG", "MDG", "MG", "Madagascar", "Madagascar", "Q1019" },
                    { (short)454, (short)0, "MWI", "MWI", "MW", "Malawi", "Malawi", "Q1020" },
                    { (short)458, (short)1, "MYS", "MYS", "MY", "Malaysia", "Malásia", "Q833" },
                    { (short)462, (short)1, "MDV", "MDV", "MV", "Maldives", "Maldivas", "Q826" },
                    { (short)466, (short)0, "MLI", "MLI", "ML", "Mali", "Mali", "Q912" },
                    { (short)470, (short)2, "MLT", "MLT", "MT", "Malta", "Malta", "Q233" },
                    { (short)474, (short)3, "MTQ", "MTQ", "MQ", "Martinique", "Martinica", null },
                    { (short)478, (short)0, "MRT", "MRT", "MR", "Mauritania", "Mauritânia", "Q1025" },
                    { (short)480, (short)0, "MUS", "MUS", "MU", "Mauritius", "Maurício", "Q1027" },
                    { (short)484, (short)3, "MEX", "MEX", "MX", "Mexico", "México", "Q96" },
                    { (short)492, (short)2, "MCO", "MCO", "MC", "Monaco", "Mônaco", "Q235" },
                    { (short)496, (short)1, "MNG", "MNG", "MN", "Mongolia", "Mongólia", "Q711" },
                    { (short)498, (short)2, "MDA", "MDA", "MD", "Moldova", "Moldávia", "Q217" },
                    { (short)499, (short)2, "MNE", "MNE", "ME", "Montenegro", "Montenegro", "Q236" },
                    { (short)500, (short)3, "MSR", "MSR", "MS", "Montserrat", "Montserrat", null },
                    { (short)504, (short)0, "MAR", "MAR", "MA", "Morocco", "Marrocos", "Q1028" },
                    { (short)508, (short)0, "MOZ", "MOZ", "MZ", "Mozambique", "Moçambique", "Q1029" },
                    { (short)512, (short)1, "OMN", "OMN", "OM", "Oman", "Omã", "Q842" },
                    { (short)516, (short)0, "NAM", "NAM", "NA", "Namibia", "Namíbia", "Q1030" },
                    { (short)520, (short)5, "NRU", "NRU", "NR", "Nauru", "Nauru", "Q697" },
                    { (short)524, (short)1, "NPL", "NPL", "NP", "Nepal", "Nepal", "Q837" },
                    { (short)528, (short)2, "NLD", "NLD", "NL", "Netherlands", "Países Baixos", "Q29999" },
                    { (short)531, (short)3, "CUW", "CUW", "CW", "Curaçao", "Curaçao", null },
                    { (short)533, (short)3, "ABW", "ABW", "AW", "Aruba", "Aruba", null },
                    { (short)534, (short)3, "SXM", "SXM", "SX", "Sint Maarten (Dutch part)", "São Martinho (parte holandesa)", null },
                    { (short)535, (short)3, "BES", "BES", "BQ", "Bonaire, Sint Eustatius and Saba", "Caribe Neerlandês", null },
                    { (short)540, (short)5, "NCL", "NCL", "NC", "New Caledonia", "Nova Caledônia", null },
                    { (short)548, (short)5, "VUT", "VUT", "VU", "Vanuatu", "Vanuatu", "Q686" },
                    { (short)554, (short)5, "NZL", "NZL", "NZ", "New Zealand", "Nova Zelândia", "Q664" },
                    { (short)558, (short)3, "NIC", "NIC", "NI", "Nicaragua", "Nicarágua", "Q811" },
                    { (short)562, (short)0, "NER", "NER", "NE", "Niger", "Níger", "Q1032" },
                    { (short)566, (short)0, "NGA", "NGA", "NG", "Nigeria", "Nigéria", "Q1033" },
                    { (short)570, (short)5, "NIU", "NIU", "NU", "Niue", "Niue", null },
                    { (short)574, (short)5, "NFK", "NFK", "NF", "Norfolk Island", "Ilha Norfolk", null },
                    { (short)578, (short)2, "NOR", "NOR", "NO", "Norway", "Noruega", "Q20" },
                    { (short)580, (short)5, "MNP", "MNP", "MP", "Northern Mariana Islands", "Ilhas Marianas do Norte", null },
                    { (short)581, (short)5, "UMI", "UMI", "UM", "United States Minor Outlying Islands", "Ilhas Menores Distantes dos Estados Unidos", null },
                    { (short)583, (short)5, "FSM", "FSM", "FM", "Micronesia", "Micronésia", "Q702" },
                    { (short)584, (short)5, "MHL", "MHL", "MH", "Marshall Islands", "Ilhas Marshall", "Q709" },
                    { (short)585, (short)5, "PLW", "PLW", "PW", "Palau", "Palau", "Q695" },
                    { (short)586, (short)1, "PAK", "PAK", "PK", "Pakistan", "Paquistão", "Q843" },
                    { (short)591, (short)3, "PAN", "PAN", "PA", "Panama", "Panamá", "Q804" },
                    { (short)598, (short)5, "PNG", "PNG", "PG", "Papua New Guinea", "Papua-Nova Guiné", "Q691" },
                    { (short)600, (short)4, "PRY", "PRY", "PY", "Paraguay", "Paraguai", "Q733" },
                    { (short)604, (short)4, "PER", "PER", "PE", "Peru", "Peru", "Q419" },
                    { (short)608, (short)1, "PHL", "PHL", "PH", "Philippines", "Filipinas", "Q928" },
                    { (short)612, (short)5, "PCN", "PCN", "PN", "Pitcairn", "Ilhas Pitcairn", null },
                    { (short)616, (short)2, "POL", "POL", "PL", "Poland", "Polônia", "Q36" },
                    { (short)620, (short)2, "PRT", "PRT", "PT", "Portugal", "Portugal", "Q45" },
                    { (short)624, (short)0, "GNB", "GNB", "GW", "Guinea-Bissau", "Guiné-Bissau", "Q1007" },
                    { (short)626, (short)1, "TLS", "TLS", "TL", "Timor-Leste", "Timor-Leste", "Q574" },
                    { (short)630, (short)3, "PRI", "PRI", "PR", "Puerto Rico", "Porto Rico", "Q1183" },
                    { (short)634, (short)1, "QAT", "QAT", "QA", "Qatar", "Catar", "Q846" },
                    { (short)638, (short)0, "REU", "REU", "RE", "Réunion", "Reunião", null },
                    { (short)642, (short)2, "ROU", "ROU", "RO", "Romania", "Romênia", "Q218" },
                    { (short)643, (short)2, "RUS", "RUS", "RU", "Russian Federation", "Rússia", "Q159" },
                    { (short)646, (short)0, "RWA", "RWA", "RW", "Rwanda", "Ruanda", "Q1037" },
                    { (short)652, (short)3, "BLM", "BLM", "BL", "Saint Barthélemy", "São Bartolomeu", null },
                    { (short)654, (short)0, "SHN", "SHN", "SH", "Saint Helena, Ascension and Tristan da Cunha", "Santa Helena, Ascensão e Tristão da Cunha", null },
                    { (short)659, (short)3, "KNA", "KNA", "KN", "Saint Kitts and Nevis", "São Cristóvão e Névis", "Q763" },
                    { (short)660, (short)3, "AIA", "AIA", "AI", "Anguilla", "Anguila", null },
                    { (short)662, (short)3, "LCA", "LCA", "LC", "Saint Lucia", "Santa Lúcia", "Q760" },
                    { (short)663, (short)3, "MAF", "MAF", "MF", "Saint Martin (French part)", "São Martinho (parte francesa)", null },
                    { (short)666, (short)3, "SPM", "SPM", "PM", "Saint Pierre and Miquelon", "São Pedro e Miquelão", null },
                    { (short)670, (short)3, "VCT", "VCT", "VC", "Saint Vincent and the Grenadines", "São Vicente e Granadinas", "Q757" },
                    { (short)674, (short)2, "SMR", "SMR", "SM", "San Marino", "San Marino", "Q238" },
                    { (short)678, (short)0, "STP", "STP", "ST", "Sao Tome and Principe", "São Tomé e Príncipe", "Q1039" },
                    { (short)682, (short)1, "SAU", "SAU", "SA", "Saudi Arabia", "Arábia Saudita", "Q851" },
                    { (short)686, (short)0, "SEN", "SEN", "SN", "Senegal", "Senegal", "Q1041" },
                    { (short)688, (short)2, "SRB", "SRB", "RS", "Serbia", "Sérvia", "Q403" },
                    { (short)690, (short)0, "SYC", "SYC", "SC", "Seychelles", "Seicheles", "Q1042" },
                    { (short)694, (short)0, "SLE", "SLE", "SL", "Sierra Leone", "Serra Leoa", "Q1044" },
                    { (short)702, (short)1, "SGP", "SGP", "SG", "Singapore", "Singapura", "Q334" },
                    { (short)703, (short)2, "SVK", "SVK", "SK", "Slovakia", "Eslováquia", "Q214" },
                    { (short)704, (short)1, "VNM", "VNM", "VN", "Viet Nam", "Vietnã", "Q881" },
                    { (short)705, (short)2, "SVN", "SVN", "SI", "Slovenia", "Eslovênia", "Q215" },
                    { (short)706, (short)0, "SOM", "SOM", "SO", "Somalia", "Somália", "Q1045" },
                    { (short)710, (short)0, "ZAF", "ZAF", "ZA", "South Africa", "África do Sul", "Q258" },
                    { (short)716, (short)0, "ZWE", "ZWE", "ZW", "Zimbabwe", "Zimbábue", "Q954" },
                    { (short)724, (short)2, "ESP", "ESP", "ES", "Spain", "Espanha", "Q29" },
                    { (short)728, (short)0, "SSD", "SSD", "SS", "South Sudan", "Sudão do Sul", "Q958" },
                    { (short)729, (short)0, "SDN", "SDN", "SD", "Sudan", "Sudão", "Q1049" },
                    { (short)732, (short)0, "ESH", "ESH", "EH", "Western Sahara", "Saara Ocidental", "Q6250" },
                    { (short)740, (short)4, "SUR", "SUR", "SR", "Suriname", "Suriname", "Q730" },
                    { (short)744, (short)2, "SJM", "SJM", "SJ", "Svalbard and Jan Mayen", "Svalbard e Jan Mayen", null },
                    { (short)748, (short)0, "SWZ", "SWZ", "SZ", "Eswatini", "Essuatíni", "Q1050" },
                    { (short)752, (short)2, "SWE", "SWE", "SE", "Sweden", "Suécia", "Q34" },
                    { (short)756, (short)2, "CHE", "CHE", "CH", "Switzerland", "Suíça", "Q39" },
                    { (short)760, (short)1, "SYR", "SYR", "SY", "Syria", "Síria", "Q858" },
                    { (short)762, (short)1, "TJK", "TJK", "TJ", "Tajikistan", "Tajiquistão", "Q863" },
                    { (short)764, (short)1, "THA", "THA", "TH", "Thailand", "Tailândia", "Q869" },
                    { (short)768, (short)0, "TGO", "TGO", "TG", "Togo", "Togo", "Q945" },
                    { (short)772, (short)5, "TKL", "TKL", "TK", "Tokelau", "Tokelau", null },
                    { (short)776, (short)5, "TON", "TON", "TO", "Tonga", "Tonga", "Q678" },
                    { (short)780, (short)3, "TTO", "TTO", "TT", "Trinidad and Tobago", "Trinidad e Tobago", "Q754" },
                    { (short)784, (short)1, "ARE", "ARE", "AE", "United Arab Emirates", "Emirados Árabes Unidos", "Q878" },
                    { (short)788, (short)0, "TUN", "TUN", "TN", "Tunisia", "Tunísia", "Q948" },
                    { (short)792, (short)1, "TUR", "TUR", "TR", "Türkiye", "Turquia", "Q43" },
                    { (short)795, (short)1, "TKM", "TKM", "TM", "Turkmenistan", "Turcomenistão", "Q874" },
                    { (short)796, (short)3, "TCA", "TCA", "TC", "Turks and Caicos Islands", "Ilhas Turcas e Caicos", null },
                    { (short)798, (short)5, "TUV", "TUV", "TV", "Tuvalu", "Tuvalu", "Q672" },
                    { (short)800, (short)0, "UGA", "UGA", "UG", "Uganda", "Uganda", "Q1036" },
                    { (short)804, (short)2, "UKR", "UKR", "UA", "Ukraine", "Ucrânia", "Q212" },
                    { (short)807, (short)2, "MKD", "MKD", "MK", "North Macedonia", "Macedônia do Norte", "Q221" },
                    { (short)818, (short)0, "EGY", "EGY", "EG", "Egypt", "Egito", "Q79" },
                    { (short)826, (short)2, "GBR", "GBR", "GB", "United Kingdom", "Reino Unido", "Q145" },
                    { (short)831, (short)2, "GGY", "GGY", "GG", "Guernsey", "Guernsey", null },
                    { (short)832, (short)2, "JEY", "JEY", "JE", "Jersey", "Jersey", null },
                    { (short)833, (short)2, "IMN", "IMN", "IM", "Isle of Man", "Ilha de Man", null },
                    { (short)834, (short)0, "TZA", "TZA", "TZ", "Tanzania", "Tanzânia", "Q924" },
                    { (short)840, (short)3, "USA", "USA", "US", "United States", "Estados Unidos", "Q30" },
                    { (short)850, (short)3, "VIR", "VIR", "VI", "Virgin Islands (U.S.)", "Ilhas Virgens Americanas", null },
                    { (short)854, (short)0, "BFA", "BFA", "BF", "Burkina Faso", "Burkina Faso", "Q965" },
                    { (short)858, (short)4, "URY", "URY", "UY", "Uruguay", "Uruguai", "Q77" },
                    { (short)860, (short)1, "UZB", "UZB", "UZ", "Uzbekistan", "Uzbequistão", "Q265" },
                    { (short)862, (short)4, "VEN", "VEN", "VE", "Venezuela", "Venezuela", "Q717" },
                    { (short)876, (short)5, "WLF", "WLF", "WF", "Wallis and Futuna", "Wallis e Futuna", null },
                    { (short)882, (short)5, "WSM", "WSM", "WS", "Samoa", "Samoa", "Q683" },
                    { (short)887, (short)1, "YEM", "YEM", "YE", "Yemen", "Iêmen", "Q805" },
                    { (short)894, (short)0, "ZMB", "ZMB", "ZM", "Zambia", "Zâmbia", "Q953" },
                    { (short)900, (short)2, "XKX", "XKX", "XK", "Kosovo", "Kosovo", "Q1246" }
                });

            migrationBuilder.InsertData(
                table: "historical_country_mapping",
                columns: new[] { "id", "country_id", "note", "wikidata_qid" },
                values: new object[,]
                {
                    { 1, (short)40, "Austria-Hungary", "Q28513" },
                    { 2, (short)40, "Austrian Empire", "Q131964" },
                    { 3, (short)643, "Soviet Union (P298 SUN)", "Q15180" },
                    { 4, (short)643, "Russian Empire", "Q34266" },
                    { 5, (short)203, "Czechoslovakia (P298 CSK)", "Q33946" },
                    { 6, (short)688, "Yugoslavia", "Q36704" },
                    { 7, (short)688, "Socialist Federal Republic of Yugoslavia (P298 YUG)", "Q83286" },
                    { 8, (short)792, "Ottoman Empire", "Q12560" },
                    { 9, (short)276, "Kingdom of Prussia", "Q27306" },
                    { 10, (short)276, "German Empire", "Q43287" },
                    { 11, (short)276, "Weimar Republic", "Q41304" },
                    { 12, (short)276, "Nazi Germany", "Q7318" },
                    { 13, (short)276, "East Germany (P298 DDR)", "Q16957" },
                    { 14, (short)276, "West Germany", "Q713750" },
                    { 15, (short)826, "United Kingdom of Great Britain and Ireland", "Q174193" },
                    { 16, (short)826, "Kingdom of Great Britain", "Q161885" },
                    { 17, (short)826, "British Empire", "Q8680" },
                    { 18, (short)528, "Netherlands (constituent country; P298 NLD is on Q29999)", "Q55" },
                    { 19, (short)208, "Kingdom of Denmark (P298 DNK is on Q35)", "Q756617" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_author_normalized_name",
                table: "author",
                column: "normalized_name");

            migrationBuilder.CreateIndex(
                name: "ix_author_country_country_id_relationship_type",
                table: "author_country",
                columns: new[] { "country_id", "relationship_type" });

            migrationBuilder.CreateIndex(
                name: "ix_book_isbn10",
                table: "book",
                column: "isbn10");

            migrationBuilder.CreateIndex(
                name: "ix_book_isbn13",
                table: "book",
                column: "isbn13",
                unique: true,
                filter: "isbn13 IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_book_author_author_id",
                table: "book_author",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_book_origin_country_country_id",
                table: "book_origin_country",
                column: "country_id");

            migrationBuilder.CreateIndex(
                name: "ix_country_iso_code",
                table: "country",
                column: "iso_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_country_iso_code2",
                table: "country",
                column: "iso_code2",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_country_wikidata_qid",
                table: "country",
                column: "wikidata_qid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_external_author_identifier_author_id",
                table: "external_author_identifier",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_external_author_identifier_provider_external_id",
                table: "external_author_identifier",
                columns: new[] { "provider", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_external_book_identifier_book_id",
                table: "external_book_identifier",
                column: "book_id");

            migrationBuilder.CreateIndex(
                name: "ix_external_book_identifier_provider_external_id",
                table: "external_book_identifier",
                columns: new[] { "provider", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_external_lookup_cache_expires_at",
                table: "external_lookup_cache",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_historical_country_mapping_country_id",
                table: "historical_country_mapping",
                column: "country_id");

            migrationBuilder.CreateIndex(
                name: "ix_historical_country_mapping_wikidata_qid",
                table: "historical_country_mapping",
                column: "wikidata_qid");

            migrationBuilder.CreateIndex(
                name: "ix_historical_country_mapping_wikidata_qid_country_id",
                table: "historical_country_mapping",
                columns: new[] { "wikidata_qid", "country_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_map_share_token",
                table: "map_share",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_map_share_user_id_map_type",
                table: "map_share",
                columns: new[] { "user_id", "map_type" });

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_token_token_hash",
                table: "password_reset_token",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_token_user_id",
                table: "password_reset_token",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_token_token_hash",
                table: "refresh_token",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_token_user_id",
                table: "refresh_token",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_email",
                table: "user",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_book_book_id",
                table: "user_book",
                column: "book_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_book_user_id_book_id",
                table: "user_book",
                columns: new[] { "user_id", "book_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_book_user_id_finished_date",
                table: "user_book",
                columns: new[] { "user_id", "finished_date" });

            migrationBuilder.CreateIndex(
                name: "ix_user_book_user_id_status",
                table: "user_book",
                columns: new[] { "user_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "author_country");

            migrationBuilder.DropTable(
                name: "book_author");

            migrationBuilder.DropTable(
                name: "book_origin_country");

            migrationBuilder.DropTable(
                name: "external_author_identifier");

            migrationBuilder.DropTable(
                name: "external_book_identifier");

            migrationBuilder.DropTable(
                name: "external_lookup_cache");

            migrationBuilder.DropTable(
                name: "historical_country_mapping");

            migrationBuilder.DropTable(
                name: "map_share");

            migrationBuilder.DropTable(
                name: "password_reset_token");

            migrationBuilder.DropTable(
                name: "refresh_token");

            migrationBuilder.DropTable(
                name: "user_book");

            migrationBuilder.DropTable(
                name: "author");

            migrationBuilder.DropTable(
                name: "country");

            migrationBuilder.DropTable(
                name: "book");

            migrationBuilder.DropTable(
                name: "user");
        }
    }
}
