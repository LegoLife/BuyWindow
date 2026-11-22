using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuyWindow.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class GroupOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GroupOrders",
                columns: table => new
                {
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupOrders", x => x.Name);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroupOrders");
        }
    }
}
