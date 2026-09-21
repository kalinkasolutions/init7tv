using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Init7Tv.Dal.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenEndedRecordings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OpenEnded",
                table: "Recordings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "OpenEnded",
                table: "PlannedRecordings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OpenEnded",
                table: "Recordings");

            migrationBuilder.DropColumn(
                name: "OpenEnded",
                table: "PlannedRecordings");
        }
    }
}
