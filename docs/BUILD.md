# Building QEMU Explorer

## Requirements

| For | You need |
|---|---|
| Building and testing | [.NET SDK](https://dotnet.microsoft.com/download) `10.0.300` or later (`global.json` pins it, rolling forward to later minor versions) |
| Running a virtual machine | [QEMU](https://www.qemu.org/download/) (`qemu-system-*`, plus `qemu-img` for the disk wizard). Not needed to build or test |
| Windows installer | [NSIS](https://nsis.sourceforge.io/) (`makensis`) |
| Linux `.deb` | `dpkg-deb` (Debian/Ubuntu, or WSL with Debian) |
| macOS `.app` | Anything with bash; a Mac for signing (see below) |

The icon and machine-type generators need PowerShell 7 (Windows only) and Python 3, respectively. Most
people never run them.

## Build, test, run

```bash
dotnet restore QemuXplorer.sln
dotnet build QemuXplorer.sln
dotnet test  QemuXplorer.sln
dotnet run --project src/QemuXplorer.App
```

The solution builds with zero warnings; keep it that way. The tests never touch your
`~/.qemu_explorer` folder.

`QemuXplorer.App.Tests` is a self-hosting xUnit v3 project. If `dotnet test` reports odd counts for it,
run it directly:

```bash
dotnet run --project tests/QemuXplorer.App.Tests/QemuXplorer.App.Tests.csproj -c Debug
```

When running from a terminal, `QEMUXPLORER_LOG_CONSOLE=1` also writes the log to the console, and
`QEMUXPLORER_LOG_LEVEL=Debug` raises the detail. Logs otherwise go to `~/.qemu_explorer/logs`.

## Publishing

Self-contained builds include the .NET runtime, so the target machine needs nothing installed. Replace
`<rid>` with `win-x64`, `win-x86`, `linux-x64`, `linux-arm64`, `osx-x64` or `osx-arm64`:

```bash
dotnet publish src/QemuXplorer.App/QemuXplorer.App.csproj -c Release -r <rid> --self-contained true -p:Version=1.0.0 -p:DebugType=none -o publish/<rid>
```

Build for the platform you are on, or cross-publish: the SDK downloads the runtime pack for the target.
Do not publish with `-p:PublishTrimmed=true`; Avalonia and EF Core rely on reflection.

## Packages

Each package is made from a publish folder. CI does exactly this; the version is `1.0.<run number>`
for a normal push and the tag for a release.

### Windows

**Zip:** zip the publish folder.

**Portable single file** (the runtime and native libraries inside one `.exe`):

```bash
dotnet publish src/QemuXplorer.App/QemuXplorer.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish/portable
```

**Installer** (NSIS; `ARCH` is `x64` or `x86`). `makensis` resolves relative paths against the `.nsi`
file's folder, so pass `SOURCE` and `OUTFILE` as absolute Windows paths:

```bash
makensis -DVERSION=1.0.0 -DARCH=x64 -DSOURCE=C:\Work\qemuXplorer\publish\win-x64 -DOUTFILE=C:\Work\qemuXplorer\dist\QemuXplorer-setup.exe tools/windows-installer.nsi
```

Use `-D` rather than `/D` in Git Bash, which would turn `/D...` into a path. `VERSION` must be numeric.

### Linux `.deb`

```bash
bash tools/build-deb.sh publish/linux-x64 dist --version 1.0.0 --arch amd64
```

`--arch` is `amd64` or `arm64`. The script stages `/opt/qemuxplorer`, a `/usr/bin/qemuxplorer` link, a
desktop entry and the icons, then calls `dpkg-deb`. Git Bash on Windows cannot create the symbolic link
and BusyBox's `dpkg-deb` cannot build, so use Linux or WSL:

```bash
wsl -d Debian -- bash tools/build-deb.sh publish/linux-x64 dist --version 1.0.0 --arch amd64
```

### macOS `.app`

```bash
bash tools/build-macos-app.sh publish/osx-arm64 dist --version 1.0.0
```

The script assembles `QemuXplorer.app` and, on a Mac, signs it ad-hoc (Apple Silicon refuses to start an
unsigned binary). Set `CODESIGN_IDENTITY` for a real Developer ID. It does not notarize, so a downloaded
app is blocked by Gatekeeper until the quarantine flag is removed:

```bash
xattr -dr com.apple.quarantine QemuXplorer.app
```

To zip it so the executable bit survives, use `ditto -c -k --keepParent QemuXplorer.app QemuXplorer.zip`.

## Database migrations

The app applies pending migrations at start-up. `dotnet-ef` is a local tool:

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/QemuXplorer.Data
```

Any change to an entity needs a migration in the same commit.

## Regenerating generated files

Do not edit these by hand.

| File | Command |
|---|---|
| `src/QemuXplorer.Models/Enums/MachineType.cs` | `python tools/dump-machine-types.py "<path to QEMU>" > src/QemuXplorer.Models/Enums/MachineType.cs` |
| `src/QemuXplorer.App/Assets/icon.*` | `pwsh tools/build-icons.ps1` (Windows) |
| `tests/QemuXplorer.Core.Tests/TestData/cpu-help/*.txt` | Saved `qemu-system-<arch> -cpu help` output from a real QEMU install |

## Making a release

1. Make sure `main` is green in [Actions](../../../actions).
2. Edit [RELEASE-NOTES.md](RELEASE-NOTES.md): rename `## Unreleased` to `## X.Y.Z (YYYY-MM-DD)` and add a
   new empty `## Unreleased` above it. The release body is exactly this section, and the workflow stops
   early if it is missing or empty. Preview it with `bash tools/release-notes.sh X.Y.Z`.
3. Commit and push that change to `main`, and wait for the run to go green.
4. Tag and push. Only `vX.Y.Z` (three numbers) starts a release:

   ```bash
   git tag -a vX.Y.Z -m "QEMU Explorer X.Y.Z"
   git push origin vX.Y.Z
   ```

5. The workflow builds every package and creates the GitHub release with ten files (Windows x64 and x86:
   installer, zip and portable exe; Linux amd64 and arm64: `.deb`; macOS arm64 and x64: zipped `.app`).
6. If it fails, fix the cause, delete the tag (`git tag -d vX.Y.Z` and `git push origin :refs/tags/vX.Y.Z`),
   and tag again. If a release was already published, do not reuse the number: ship `X.Y.Z+1`.

## Line endings

Shell scripts must keep LF endings or bash fails on Linux and macOS (`set: pipefail: invalid option`).
`.gitattributes` enforces this; if you create a new script on Windows, check it with `git ls-files --eol`.
