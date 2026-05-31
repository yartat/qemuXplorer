# QEmuXlorer QEMU Frontend
[![QEMU Explorer](https://github.com/yartat/qemuXplorer/actions/workflows/dotnet-desktop.yml/badge.svg)](https://github.com/yartat/qemuXplorer/actions/workflows/dotnet-desktop.yml)

A cross-platform UI shell for QEMU built with .NET 10 and Avalonia UI 11.

## Features
- Launch and manage QEMU virtual machines (Windows, Linux, macOS)
- Rich VM configuration: CPU, memory, storage, networking, display, USB, PCI
- Supports all major QEMU architectures (x86_64, ARM64, RISC-V, PPC, MIPS, s390x)
- Accelerator support: KVM (Linux), HVF (macOS), WHPX (Windows), TCG fallback
- SQLite-backed persistent configuration
- Auto-detection of installed QEMU binaries
- Generated command preview before launch

## Requirements
- .NET 10 SDK (`10.0.300` or later)
- QEMU installed on the host system

## Getting Started

```bash
# Restore and build
dotnet restore
dotnet build

# Run the app
dotnet run --project src/QEmuXlorer.App

# Run tests
dotnet test
```

## First-Time Setup
1. On first launch the SQLite database is created automatically at:
   - **Windows**: `%APPDATA%\QEmuXlorer\data.db`
   - **macOS**: `~/Library/Application Support/QEmuXlorer/data.db`
   - **Linux**: `~/.local/share/QEmuXlorer/data.db`
2. Go to **QEMU Installations** and click **Auto-Detect** to find your QEMU binaries.
3. Create a Virtual Machine and configure it.
4. Click **Start** to launch.

## EF Core Migrations

```bash
# From the repo root
dotnet ef migrations add <MigrationName> --project src/QEmuXlorer.Data --startup-project src/QEmuXlorer.App
dotnet ef database update              --project src/QEmuXlorer.Data --startup-project src/QEmuXlorer.App
```

## Solution Structure

```
src/
  QEmuXlorer.Models/    Domain entities and enumerations
  QEmuXlorer.Data/      EF Core DbContext, migrations, repositories
  QEmuXlorer.Core/      Business logic, QEMU process management, argument builder
  QEmuXlorer.App/       Avalonia UI application (cross-platform)
tests/
  QEmuXlorer.Core.Tests/ Unit tests
```
