using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StructRAG.Store.Migrations
{
    /// <inheritdoc />
    public partial class InitialSubstrate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "structrag");

            migrationBuilder.CreateTable(
                name: "algorithms",
                schema: "structrag",
                columns: table => new
                {
                    key = table.Column<string>(type: "TEXT", nullable: false),
                    document_id = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    steps = table.Column<string>(type: "TEXT", nullable: false),
                    source_chunk_key = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_algorithms", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "catalogue_items",
                schema: "structrag",
                columns: table => new
                {
                    key = table.Column<string>(type: "TEXT", nullable: false),
                    document_id = table.Column<string>(type: "TEXT", nullable: false),
                    category = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    attributes = table.Column<string>(type: "TEXT", nullable: false),
                    source_chunk_key = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalogue_items", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "entities",
                schema: "structrag",
                columns: table => new
                {
                    key = table.Column<string>(type: "TEXT", nullable: false),
                    document_id = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    type = table.Column<string>(type: "TEXT", nullable: false),
                    source_chunk_key = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entities", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "events",
                schema: "structrag",
                columns: table => new
                {
                    key = table.Column<string>(type: "TEXT", nullable: false),
                    document_id = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    date = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    participant_entity_keys = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_events", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "evidence",
                schema: "structrag",
                columns: table => new
                {
                    key = table.Column<string>(type: "TEXT", nullable: false),
                    document_id = table.Column<string>(type: "TEXT", nullable: false),
                    chunk_key = table.Column<string>(type: "TEXT", nullable: false),
                    quote = table.Column<string>(type: "TEXT", nullable: false),
                    fact_key = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evidence", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "facts",
                schema: "structrag",
                columns: table => new
                {
                    key = table.Column<string>(type: "TEXT", nullable: false),
                    document_id = table.Column<string>(type: "TEXT", nullable: false),
                    subject = table.Column<string>(type: "TEXT", nullable: false),
                    predicate = table.Column<string>(type: "TEXT", nullable: false),
                    @object = table.Column<string>(name: "object", type: "TEXT", nullable: false),
                    occurred_on = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    evidence_chunk_keys = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_facts", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "substrate_metadata",
                schema: "structrag",
                columns: table => new
                {
                    document_id = table.Column<string>(type: "TEXT", nullable: false),
                    extraction_version = table.Column<string>(type: "TEXT", nullable: false),
                    last_built = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_substrate_metadata", x => x.document_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Algorithms_DocumentId",
                schema: "structrag",
                table: "algorithms",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogueItems_DocumentId",
                schema: "structrag",
                table: "catalogue_items",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "IX_Entities_DocumentId",
                schema: "structrag",
                table: "entities",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "IX_Events_DocumentId",
                schema: "structrag",
                table: "events",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "IX_Evidence_ChunkFact",
                schema: "structrag",
                table: "evidence",
                columns: new[] { "chunk_key", "fact_key" });

            migrationBuilder.CreateIndex(
                name: "IX_Evidence_ChunkKey",
                schema: "structrag",
                table: "evidence",
                column: "chunk_key");

            migrationBuilder.CreateIndex(
                name: "IX_Facts_DocumentId",
                schema: "structrag",
                table: "facts",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "IX_Facts_Predicate",
                schema: "structrag",
                table: "facts",
                column: "predicate");

            migrationBuilder.CreateIndex(
                name: "IX_Facts_Subject",
                schema: "structrag",
                table: "facts",
                column: "subject");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "algorithms",
                schema: "structrag");

            migrationBuilder.DropTable(
                name: "catalogue_items",
                schema: "structrag");

            migrationBuilder.DropTable(
                name: "entities",
                schema: "structrag");

            migrationBuilder.DropTable(
                name: "events",
                schema: "structrag");

            migrationBuilder.DropTable(
                name: "evidence",
                schema: "structrag");

            migrationBuilder.DropTable(
                name: "facts",
                schema: "structrag");

            migrationBuilder.DropTable(
                name: "substrate_metadata",
                schema: "structrag");
        }
    }
}
