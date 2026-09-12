using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QemuXplorer.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "QemuInstallations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    BinaryDirectory = table.Column<string>(type: "TEXT", nullable: false),
                    DetectedVersion = table.Column<string>(type: "TEXT", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    Platform = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QemuInstallations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VirtualMachines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Architecture = table.Column<int>(type: "INTEGER", nullable: false),
                    MachineType = table.Column<string>(type: "TEXT", nullable: false),
                    EnableKvm = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnableHvf = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnableWhpx = table.Column<bool>(type: "INTEGER", nullable: false),
                    FallbackToTcg = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExtraArguments = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsTemplate = table.Column<bool>(type: "INTEGER", nullable: false),
                    Cpu_Model = table.Column<string>(type: "TEXT", nullable: false),
                    Cpu_Count = table.Column<int>(type: "INTEGER", nullable: false),
                    Cpu_Sockets = table.Column<int>(type: "INTEGER", nullable: false),
                    Cpu_Cores = table.Column<int>(type: "INTEGER", nullable: false),
                    Cpu_Threads = table.Column<int>(type: "INTEGER", nullable: false),
                    Memory_SizeMB = table.Column<long>(type: "INTEGER", nullable: false),
                    Memory_HotplugSlots = table.Column<int>(type: "INTEGER", nullable: false),
                    Display_Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Display_VgaModel = table.Column<int>(type: "INTEGER", nullable: false),
                    Display_VncAddress = table.Column<string>(type: "TEXT", nullable: true),
                    Display_VncPort = table.Column<int>(type: "INTEGER", nullable: false),
                    Display_SpicePort = table.Column<int>(type: "INTEGER", nullable: false),
                    Display_IsFullscreen = table.Column<bool>(type: "INTEGER", nullable: false),
                    Display_IsGLEnabled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VirtualMachines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DiskDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VmId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", nullable: false),
                    Format = table.Column<int>(type: "INTEGER", nullable: false),
                    Interface = table.Column<int>(type: "INTEGER", nullable: false),
                    CacheMode = table.Column<int>(type: "INTEGER", nullable: false),
                    IsBootDevice = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsReadOnly = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsCdRom = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSnapshotMode = table.Column<bool>(type: "INTEGER", nullable: false),
                    Index = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiskDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiskDevices_VirtualMachines_VmId",
                        column: x => x.VmId,
                        principalTable: "VirtualMachines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NetworkAdapters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VmId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BackendType = table.Column<int>(type: "INTEGER", nullable: false),
                    Model = table.Column<int>(type: "INTEGER", nullable: false),
                    MacAddress = table.Column<string>(type: "TEXT", nullable: false),
                    HostForwardingRules = table.Column<string>(type: "TEXT", nullable: false),
                    BridgeInterface = table.Column<string>(type: "TEXT", nullable: false),
                    TapInterface = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NetworkAdapters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NetworkAdapters_VirtualMachines_VmId",
                        column: x => x.VmId,
                        principalTable: "VirtualMachines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UsbDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VmId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VendorId = table.Column<ushort>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<ushort>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    IsPassthrough = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsbDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UsbDevices_VirtualMachines_VmId",
                        column: x => x.VmId,
                        principalTable: "VirtualMachines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiskDevices_VmId",
                table: "DiskDevices",
                column: "VmId");

            migrationBuilder.CreateIndex(
                name: "IX_NetworkAdapters_VmId",
                table: "NetworkAdapters",
                column: "VmId");

            migrationBuilder.CreateIndex(
                name: "IX_UsbDevices_VmId",
                table: "UsbDevices",
                column: "VmId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "DiskDevices");

            migrationBuilder.DropTable(
                name: "NetworkAdapters");

            migrationBuilder.DropTable(
                name: "QemuInstallations");

            migrationBuilder.DropTable(
                name: "UsbDevices");

            migrationBuilder.DropTable(
                name: "VirtualMachines");
        }
    }
}
