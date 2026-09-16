using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Init7Tv.Dal.Migrations
{
    /// <inheritdoc />
    public partial class AddFavouriteChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FavouriteChannels",
                columns: table => new
                {
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ChannelId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FavouriteChannels", x => new { x.UserName, x.ChannelId });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FavouriteChannels");
        }
    }
}
