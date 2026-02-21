using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Init7Tv.Dal.Migrations
{
    /// <inheritdoc />
    public partial class UpdateGeneralConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FfmpegLogLevel",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 10,
                nullable: false,
                defaultValue: "warning");

            migrationBuilder.AddColumn<string>(
                name: "FfmpegPreset",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 10,
                nullable: false,
                defaultValue: "ultrafast");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FfmpegLogLevel",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "FfmpegPreset",
                table: "AppSettings");
        }
    }
}
