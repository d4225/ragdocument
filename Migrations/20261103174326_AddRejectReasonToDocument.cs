using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartDocumentRAG.API.Migrations
{
    /// <inheritdoc />
    public partial class AddRejectReasonToDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RejectReason",
                table: "Documents",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RejectReason",
                table: "Documents");
        }
    }
}
