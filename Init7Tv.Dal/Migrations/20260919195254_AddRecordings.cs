using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Init7Tv.Dal.Migrations
{
    /// <inheritdoc />
    public partial class AddRecordings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RecordingPostRollMinutes",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                // the entity's own defaults, so an existing install records
                // sensibly before anyone visits the settings page
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "RecordingPreRollMinutes",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<string>(
                name: "RecordingPreset",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 10,
                nullable: false,
                defaultValue: "veryfast");

            migrationBuilder.CreateTable(
                name: "Recordings",
                columns: table => new
                {
                    RecordingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProgrammeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ChannelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChannelName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    CanonicalName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    SubTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ScheduledStart = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ScheduledEnd = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    Directory = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recordings", x => x.RecordingId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Recordings_ProgrammeId",
                table: "Recordings",
                column: "ProgrammeId");

            migrationBuilder.CreateIndex(
                name: "IX_Recordings_State",
                table: "Recordings",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_Recordings_UserName_State",
                table: "Recordings",
                columns: new[] { "UserName", "State" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Recordings");

            migrationBuilder.DropColumn(
                name: "RecordingPostRollMinutes",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "RecordingPreRollMinutes",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "RecordingPreset",
                table: "AppSettings");
        }
    }
}
