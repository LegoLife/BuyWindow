using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuyWindow.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropCpiConsensus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CpiConsensus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CpiConsensus",
                columns: table => new
                {
                    EventTicker = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CpiConsensus", x => x.EventTicker);
                });
        }
    }
}
