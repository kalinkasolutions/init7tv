using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Init7Tv.Dal.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUnusedEnableSsl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnableSsl",
                table: "AppSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnableSsl",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }
    }
}
