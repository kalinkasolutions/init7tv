using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Init7Tv.Dal.Migrations
{
    /// <inheritdoc />
    public partial class AddFfmpegDeinterlaceMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FfmpegDeinterlaceMode",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "send_field");

            // AddColumn's default does not reach rows written before it existed
            migrationBuilder.Sql(
                "UPDATE AppSettings SET FfmpegDeinterlaceMode = 'send_field' " +
                "WHERE FfmpegDeinterlaceMode IS NULL OR FfmpegDeinterlaceMode = ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FfmpegDeinterlaceMode",
                table: "AppSettings");
        }
    }
}
