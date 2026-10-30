# QemuXplorer — QEMU Frontend
[![QEMU Explorer](https://github.com/yartat/qemuXplorer/actions/workflows/dotnet-desktop.yml/badge.svg)](https://github.com/yartat/qemuXplorer/actions/workflows/dotnet-desktop.yml)

A cross-platform desktop UI for QEMU, built with .NET 10 and Avalonia UI 12.1.

Define virtual machines in a GUI, keep them in a local SQLite database, preview the exact
`qemu-system-*` command line that will be produced, and launch and monitor the process.

## Features
- Launch and manage QEMU virtual machines on Windows, Linux and macOS
- Rich VM configuration: CPU topology, memory, disks, networking, display, USB passthrough
- Supports the major QEMU guest architectures (x86_64, i386, ARM/ARM64, RISC-V, PPC, MIPS, s390x)
- Accelerator selection: KVM (Linux), HVF (macOS), WHPX (Windows), with TCG fallback
- SQLite-backed persistent configuration, created automatically on first run
- Auto-detection of installed QEMU binaries in the usual locations
- Generated command preview before launch
- Wizards for network adapters (NAT with port forwarding, tap, bridge, sockets) and USB devices (passthrough picked from
  what is plugged in, USB sticks, input devices, audio, serial, smart cards, controllers), each editable by its type
- Edit a disk's image from the Storage tab: resize, convert, rebase, amend or check it with `qemu-img`
- Clone an existing VM as a starting point for a new one

## Requirements
- [QEMU](https://www.qemu.org/download/) installed on the host system (`qemu-system-*` and, for disk image
  management, `qemu-img`). It is not bundled.
- To build from source only: the [.NET 10 SDK](https://dotnet.microsoft.com/download) `10.0.300` or later.
  The downloads below include the runtime.

## Download

Ready-to-run builds are attached to every [release](../../releases/latest), and what changed is in the
[release notes](docs/RELEASE-NOTES.md). Pick the file for your system:

| System | File | Notes |
|---|---|---|
| **Windows** (64-bit) | `QemuXplorer-<version>-win-x64-setup.exe` | Installer, adds a Start menu entry and an uninstaller |
| | `QemuXplorer-<version>-win-x64.zip` | Unpack anywhere and run `QemuXplorer.exe` |
| | `QemuXplorer-<version>-win-x64-portable.exe` | A single file, nothing to install; unpacks its libraries on first start |
| **Windows** (32-bit) | the same three files with `win-x86` | |
| **Linux** (Debian, Ubuntu) | `qemuxplorer_<version>_amd64.deb` | For Intel/AMD processors |
| | `qemuxplorer_<version>_arm64.deb` | For Arm processors |
| **macOS** (Apple Silicon) | `QemuXplorer-<version>-osx-arm64.zip` | Contains `QemuXplorer.app` |
| **macOS** (Intel) | `QemuXplorer-<version>-osx-x64.zip` | Contains `QemuXplorer.app` |

The builds include the .NET runtime, so you do not need to install .NET. QEMU itself is not
included: install it separately (see [Requirements](#requirements)).

### Windows

Run the installer, or unpack the zip, or just start the portable exe. If SmartScreen says the
publisher is unknown, choose **More info → Run anyway**: the builds are not code-signed.

### Linux

```bash
sudo apt install ./qemuxplorer_<version>_amd64.deb
```

Then start **QEMU Explorer** from the application menu, or run `qemuxplorer`.
Remove it with `sudo apt remove qemuxplorer`.

### macOS

Unzip and move `QemuXplorer.app` to **Applications**. The app is not notarized, so macOS blocks it
after a download. Remove the block once:

```bash
xattr -dr com.apple.quarantine /Applications/QemuXplorer.app
```

### Where your data lives

Machines, settings and logs are stored in `~/.qemu_explorer` (on Windows, `C:\Users\<you>\.qemu_explorer`),
not next to the program. Updating or reinstalling keeps them; uninstalling does not delete them.
Nothing is sent anywhere: see [PRIVACY.md](PRIVACY.md).

## Building from source

```bash
dotnet build QemuXplorer.sln
dotnet test  QemuXplorer.sln
dotnet run --project src/QemuXplorer.App
```

[docs/BUILD.md](docs/BUILD.md) covers publishing, building the Windows installer, `.deb` and macOS app,
regenerating the generated files, and making a release.

## First-Time Setup
1. On first launch the SQLite database is created and migrated automatically at
   `~/.qemu_explorer/data.db` (on Windows, `%USERPROFILE%\.qemu_explorer\data.db`).
2. Go to **QEMU Installations** and click **Auto-Detect** to find your QEMU binaries. The first
   one found is marked as the default; use **Set Default** to choose a different one. A default
   installation is required before any VM can start. Re-running detection will not create
   duplicate entries.
3. Create a Virtual Machine and configure CPU, memory, disks, network and display.
4. Use the command preview to check the generated arguments, then click **Start**.

## Logs

The application writes its log to a file, not the console:
`~/.qemu_explorer/logs/qemuxplorer-<date>.log` (on Windows, `%USERPROFILE%\.qemu_explorer\logs`). Files
roll daily and the last 14 are kept.

| Variable | Effect |
|---|---|
| `QEMUXPLORER_LOG_CONSOLE=1` | Also write to the console, e.g. when running from a terminal |
| `QEMUXPLORER_LOG_LEVEL` | `Trace`, `Debug`, `Information` (default), `Warning`, `Error` or `Critical` |

## Solution Structure

```
src/
  QemuXplorer.Models/     Domain entities, enumerations and DTOs (no dependencies)
  QemuXplorer.Data/       EF Core DbContext, repositories, database path resolution
  QemuXplorer.Core/       QEMU argument builder, process manager, discovery, disk images
  QemuXplorer.App/        Avalonia UI application (Views, ViewModels, DI composition root)
tests/
  QemuXplorer.Core.Tests/ Unit tests for the Core services, repositories and argument builder
  QemuXplorer.App.Tests/  Headless Avalonia tests (views, view models, logging)
docs/                     Build guide, release notes, third-party attribution
tools/                    Packaging and code-generation scripts
```

Dependencies flow in one direction: `Models` ← `Data` ← `Core` ← `App`.

The heart of the application is `QemuArgumentBuilder`, a pure translation from a
`VirtualMachine` entity to an argument list. Arguments are passed to the process through
`ProcessStartInfo.ArgumentList`, so no shell is involved and no quoting is required.

## Database Schema

The schema is managed with EF Core migrations, which live in
`src/QemuXplorer.Data/Migrations`. The application applies any pending migration on startup,
so a database created by an earlier version is upgraded in place.

`dotnet-ef` is pinned as a local tool, so restore it once before using it:

```bash
dotnet tool restore
```

Then, from the repository root:

```bash
dotnet ef migrations add <MigrationName> --project src/QemuXplorer.Data
dotnet ef database update               --project src/QemuXplorer.Data
```

No `--startup-project` is required — `DesignTimeDbContextFactory` supplies the context, since
the Avalonia entry point does not use a generic host.

## Build Configuration

- `global.json` pins the SDK to `10.0.300` with `rollForward: latestMinor`.
- `Directory.Build.props` sets the target framework and language options for every project.
- `Directory.Packages.props` manages all package versions centrally, with floating versions
  enabled. Project files reference packages without a `Version` attribute; new dependencies
  are added as a `PackageVersion` entry there.

## Documentation

| | |
|---|---|
| [docs/BUILD.md](docs/BUILD.md) | Building, packaging and releasing |
| [docs/RELEASE-NOTES.md](docs/RELEASE-NOTES.md) | What changed in each version |
| [PRIVACY.md](PRIVACY.md) | What the app stores and what it connects to |
| [docs/ATTRIBUTION.md](docs/ATTRIBUTION.md) | Third-party software and licenses |

## License

MIT — see [LICENSE](LICENSE). Third-party components are listed in [docs/ATTRIBUTION.md](docs/ATTRIBUTION.md).
QEMU is a separate program under its own license and is not affiliated with this project.
