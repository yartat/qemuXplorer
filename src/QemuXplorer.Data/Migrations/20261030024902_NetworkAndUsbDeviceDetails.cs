using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QemuXplorer.Data.Migrations
{
    /// <inheritdoc />
    public partial class NetworkAndUsbDeviceDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AudioDriver",
                table: "UsbDevices",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Controller",
                table: "UsbDevices",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DeviceType",
                table: "UsbDevices",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HostAddress",
                table: "UsbDevices",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HostBus",
                table: "UsbDevices",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ImageFormat",
                table: "UsbDevices",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ImagePath",
                table: "UsbDevices",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsBootDevice",
                table: "UsbDevices",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsReadOnly",
                table: "UsbDevices",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsRemovable",
                table: "UsbDevices",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SerialBackend",
                table: "UsbDevices",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SerialPort",
                table: "UsbDevices",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SerialTarget",
                table: "UsbDevices",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SocketHost",
                table: "NetworkAdapters",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SocketLocalHost",
                table: "NetworkAdapters",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "SocketLocalPort",
                table: "NetworkAdapters",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SocketMode",
                table: "NetworkAdapters",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SocketPort",
                table: "NetworkAdapters",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TapDownScript",
                table: "NetworkAdapters",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TapScript",
                table: "NetworkAdapters",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UserDhcpStart",
                table: "NetworkAdapters",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UserDns",
                table: "NetworkAdapters",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UserHostname",
                table: "NetworkAdapters",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "UserRestrict",
                table: "NetworkAdapters",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "UserSubnet",
                table: "NetworkAdapters",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AudioDriver",
                table: "UsbDevices");

            migrationBuilder.DropColumn(
                name: "Controller",
                table: "UsbDevices");

            migrationBuilder.DropColumn(
                name: "DeviceType",
                table: "UsbDevices");

            migrationBuilder.DropColumn(
                name: "HostAddress",
                table: "UsbDevices");

            migrationBuilder.DropColumn(
                name: "HostBus",
                table: "UsbDevices");

            migrationBuilder.DropColumn(
                name: "ImageFormat",
                table: "UsbDevices");

            migrationBuilder.DropColumn(
                name: "ImagePath",
                table: "UsbDevices");

            migrationBuilder.DropColumn(
                name: "IsBootDevice",
                table: "UsbDevices");

            migrationBuilder.DropColumn(
                name: "IsReadOnly",
                table: "UsbDevices");

            migrationBuilder.DropColumn(
                name: "IsRemovable",
                table: "UsbDevices");

            migrationBuilder.DropColumn(
                name: "SerialBackend",
                table: "UsbDevices");

            migrationBuilder.DropColumn(
                name: "SerialPort",
                table: "UsbDevices");

            migrationBuilder.DropColumn(
                name: "SerialTarget",
                table: "UsbDevices");

            migrationBuilder.DropColumn(
                name: "SocketHost",
                table: "NetworkAdapters");

            migrationBuilder.DropColumn(
                name: "SocketLocalHost",
                table: "NetworkAdapters");

            migrationBuilder.DropColumn(
                name: "SocketLocalPort",
                table: "NetworkAdapters");

            migrationBuilder.DropColumn(
                name: "SocketMode",
                table: "NetworkAdapters");

            migrationBuilder.DropColumn(
                name: "SocketPort",
                table: "NetworkAdapters");

            migrationBuilder.DropColumn(
                name: "TapDownScript",
                table: "NetworkAdapters");

            migrationBuilder.DropColumn(
                name: "TapScript",
                table: "NetworkAdapters");

            migrationBuilder.DropColumn(
                name: "UserDhcpStart",
                table: "NetworkAdapters");

            migrationBuilder.DropColumn(
                name: "UserDns",
                table: "NetworkAdapters");

            migrationBuilder.DropColumn(
                name: "UserHostname",
                table: "NetworkAdapters");

            migrationBuilder.DropColumn(
                name: "UserRestrict",
                table: "NetworkAdapters");

            migrationBuilder.DropColumn(
                name: "UserSubnet",
                table: "NetworkAdapters");
        }
    }
}
