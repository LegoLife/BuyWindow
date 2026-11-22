using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuyWindow.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class WatchlistGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Group",
                table: "Symbols",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Group",
                table: "Symbols");
        }
    }
}
