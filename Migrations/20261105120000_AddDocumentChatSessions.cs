using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SmartDocumentRAG.API.Data;

#nullable disable

namespace SmartDocumentRAG.API.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261105120000_AddDocumentChatSessions")]
public partial class AddDocumentChatSessions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "DocumentId",
            table: "ChatSessions",
            type: "uuid",
            nullable: true);

    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DocumentId",
            table: "ChatSessions");
    }
}
