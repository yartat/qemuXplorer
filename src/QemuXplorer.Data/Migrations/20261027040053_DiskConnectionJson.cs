using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QemuXplorer.Data.Migrations
{
    /// <inheritdoc />
    public partial class DiskConnectionJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConnectionJson",
                table: "DiskDevices",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConnectionJson",
                table: "DiskDevices");
        }
    }
}
