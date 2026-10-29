# CLAUDE.md

Guidance for Claude Code when working in this repository.

## What this is

A cross-platform desktop frontend for QEMU: define virtual machines in a UI, persist them
to SQLite, translate them into a `qemu-system-*` command line, and launch/track the process.

.NET 10 · Avalonia 12.1 · EF Core 10 (SQLite) · CommunityToolkit.Mvvm · xUnit.

## Naming

Everything is spelled `QemuXplorer` — solution, projects, assemblies, namespaces, the
`QemuXplorerDbContext`. The only lowercase variant is the git repository directory itself,
`qemuXplorer`. The user-facing product name is "QEMU Explorer" and the data directory is
`~/.qemu_explorer`.

If you find a stray `QEmuXlorer` or `QEmuXplorer`, it is a leftover — fix it.

## Layout and dependency direction

```
QemuXplorer.Models  ← entities, enums, DTOs. No dependencies at all.
QemuXplorer.Data    ← DbContext, Migrations/, repositories, DB path resolution,
                     DesignTimeDbContextFactory. References Models.
QemuXplorer.Core    ← services: argument building, process management, discovery, disk images.
                     References Models + Data.
QemuXplorer.App     ← Avalonia UI: Views (.axaml) + ViewModels + DI composition root.
                     References all three.
tests/QemuXplorer.Core.Tests ← project-references Core + Models (Data comes in transitively
                     through Core, so Data types are usable without a direct reference).
tests/QemuXplorer.App.Tests  ← headless Avalonia tests. References App.
```

Dependencies flow one way. Do not make Models or Data reference Core or App.

Repo root also carries `Directory.Build.props`, `Directory.Packages.props`, `global.json`,
`NuGet.config` and `.config/dotnet-tools.json` — they all affect every build, so check them
before assuming a setting comes from a `.csproj`.

## Build, test, run

```bash
dotnet build QemuXplorer.sln
dotnet test  QemuXplorer.sln
dotnet run --project src/QemuXplorer.App
```

The full solution builds clean with zero warnings — keep it that way. `dotnet test` on the
solution runs both test projects: 212 in Core.Tests (argument builder, machine and disk catalogs,
disk image, CPU model and CPU description services, repository) and 115 headless UI tests (view loading, window icons, icon files,
machine and CPU pickers, disk wizard, logging).

## Package management

Central Package Management is on (`Directory.Packages.props`), with floating versions enabled.

- `<PackageReference Include="X" />` in a `.csproj` — **never** with a `Version` attribute.
- New dependency → add a `<PackageVersion Include="X" Version="N.N.*" />` entry to
  `Directory.Packages.props`, then reference it without a version.
- `Directory.Build.props` already sets `TargetFramework`, `Nullable`, `ImplicitUsings`,
  `LangVersion` for every project. Don't redeclare them per project.
- Because versions float on `*`, a restore can silently pick up a new minor. If something
  breaks after a clean restore, compare resolved versions in `obj/project.assets.json`.

## Database: migrations

The database lives at `~/.qemu_explorer/data.db` on every platform (`DbPathHelper`).

`DatabaseInitializer.InitializeAsync` runs `MigrateAsync()` on startup and then seeds three
`AppSettings` rows if the table is empty. Migrations live in `src/QemuXplorer.Data/Migrations`.

`dotnet-ef` is pinned as a **local** tool, so it needs a restore before first use:

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/QemuXplorer.Data
dotnet ef database update        --project src/QemuXplorer.Data
```

No `--startup-project` is needed: `DesignTimeDbContextFactory` exists precisely because the
Avalonia entry point has no generic host for EF to discover.

**Any entity change needs a migration in the same commit.** The app only ever applies
migrations — it will not silently reshape an existing database.

Owned types: `Cpu`, `Memory` and `Display` are configured with `OwnsOne`, so they flatten into
the `VirtualMachines` table as `Cpu_Model`, `Memory_SizeMB`, `Display_Type` and so on.

## The argument builder is the core logic

`QemuArgumentBuilder.Build(vm, qemu)` is pure, dependency-free, and the only place QEMU
command-line syntax lives. It is the one class with real test coverage
(`tests/QemuXplorer.Core.Tests/Services/QemuArgumentBuilderTests.cs`, 24 tests).

Rules when touching it:

- Every enum → string conversion goes through the `private static` mapper methods at the
  bottom of the file (`InterfaceName`, `CacheName`, …). Adding an enum member
  means adding a case there — the `_ =>` fallbacks will otherwise silently pick a default.
- QEMU driver names are not the enum names. `DiskFormat.Vhd` is `vpc` on the command line and
  in `qemu-img` output. Disk format names live in one place, `DiskFormatCatalog.QemuName`, which
  the builder and `DiskImageService` both use — never re-map them locally.
- Arguments are returned as a `IReadOnlyList<string>` and passed to
  `ProcessStartInfo.ArgumentList`, so there is **no shell and no quoting**. Never build a
  single space-joined command string for execution.
  (`VmEditViewModel.RefreshPreview` joins them for *display* only.)
- `AddDisplay` emits **exactly one** of `-display` / `-vnc` / `-spice`. Keep it that way: the
  earlier version emitted `-display vnc` alongside `-vnc`, and a second `-display` whenever GL
  was enabled.
- NVMe disks are not an `if=` target. They emit `-drive ...,if=none,id=driveN` plus a matching
  `-device nvme,drive=driveN`; without the second half the drive is attached to nothing.
- Add a test for any new flag. Existing tests assert on the presence of a flag or a substring
  of its value, using FluentAssertions.

## Machine types and their architectures

`MachineType` (127 members) is **generated**, not hand-written: `tools/dump-machine-types.py`
parses `qemu-system-<arch> -M help` from a real QEMU install. Do not add members by hand — point
the script at a QEMU build and regenerate:

```bash
python tools/dump-machine-types.py "/c/Program Files/qemu" > src/QemuXplorer.Models/Enums/MachineType.cs
```

Three facts drive the design:

- **The relation is many-to-many.** 96 of the 127 machines are offered by more than one
  architecture — `virt` by aarch64/arm/riscv64/riscv32, `none` by all eleven. A single
  `Display.GroupName` string cannot express this, so `MachineAttribute` carries a
  `GuestArchitecture[]` instead. `[Display]` keeps the human name and description.
- **QEMU names are not valid C# identifiers** (`ast2500-evb`, `40p`, `b-l475e-iot01a`), so
  `MachineAttribute.QemuName` is the authority for what goes on the command line. Never derive
  the QEMU name from the enum member.
- **`VirtualMachine.MachineType` stays a `string`.** QEMU also accepts versioned variants
  (`pc-q35-9.2`) that the enum deliberately omits, and a different QEMU build may offer machines
  this list has never heard of. An enum column would make those unusable and would need a
  migration; the string needs neither.

`MachineCatalog` reflects over the enum once and answers lookups from memory: `ForArchitecture`,
`DefaultFor`, `Find`, `IsValidFor`. **`Find` returning null means "not catalogued", not
"invalid"** — treat it as unverifiable, never as an error.

In the editor, changing architecture refilters the picker and replaces a machine the new
emulator cannot run. Loading a saved machine does **not** auto-correct: it shows a warning
instead, because silently rewriting stored data on open hides the problem.

The list is a snapshot of one build. Reading `-M help` from the selected installation at runtime
would be strictly more correct and is the natural next step.

## CPU models

The editor's CPU "Model" is a picker, not a text box. `CpuModelService` fills it by running
`qemu-system-<arch> -cpu help` from the default installation and parsing the `Available CPUs:`
block (`CpuModelService.Parse`).

- **The list depends on architecture and QEMU build, not on the machine type.** The output is
  byte-identical for aarch64 under `virt`, `raspi3b` and `sbsa-ref`, so the picker is keyed on
  architecture only. Whether a *machine* accepts a given CPU is decided when QEMU starts; nothing
  here can tell you in advance.
- **`host` is always listed first; the "(machine default)" entry second.** Most architectures do not
  list `host` themselves, so the service adds it. The machine default has an **empty name** and means
  "pass no `-cpu`": QEMU then uses the CPU the selected machine is built around. The argument builder
  omits `-cpu` for a blank model.
- **`host` is the default only when this computer has the guest's architecture**
  (`HostPlatform.CanUseHostCpu`, via `CpuModelService.DefaultModelName`); otherwise the default is the
  machine default. Across architectures QEMU emulates and `-cpu host` fails (`unable to find CPU model
  'host'`); without an accelerator it fails too (`requires KVM or HVF`). With `-accel whpx` it works on
  Windows. On QEMU 11.1 all 14 tried machines start with no `-cpu`.
- `HostPlatform` uses `RuntimeInformation.OSArchitecture` (the CPU, not this process) and is
  deliberately conservative: x64 matches x86_64 and i386; Arm64 matches aarch64 only, because 32-bit
  Arm guests need 32-bit support most Arm64 cores no longer have.
- **An untouched default follows the architecture; a choice does not.** In the editor, if the model
  still equals the old architecture's default it becomes the new one's; a model the user picked stays
  while the new emulator lists it. `VirtualMachinesViewModel.AddVmAsync` starts a new machine with the
  default for its architecture. `CpuConfig.Model` stays `"host"` as the entity's own fallback because
  Models cannot know the host. Existing machines are never rewritten.
- **Never rewrite a saved model just by opening the editor.** A saved value the build does not list
  is kept and shown as "not listed by this QEMU build". Only an explicit *architecture change*
  replaces an unlisted model with that architecture's default, and only when the list was actually read:
  a failed probe knows nothing, so it must not discard the user's choice.
- Results are cached per binary for the process lifetime; failures are not cached. The shared load
  deliberately ignores any one caller's cancellation token.
- With no default installation, or if QEMU cannot be run, the picker degrades to `host`, the machine
  default and the saved value, and shows why in `CpuModelsNote`.
- `QemuArgumentBuilder.GetBinaryName` is the one architecture-to-binary table and is public so the
  probe uses it too.
- Parsing stops at the first blank line. s390x follows the model list with "Recognized feature
  flags" sections that would otherwise be read as hundreds of bogus models.
- The list includes aliases and versioned variants (`Broadwell-IBRS`, `Broadwell-v3`), all valid
  `-cpu` values. x86_64 reports 189, ppc64 416.

### CPU model descriptions

`-cpu help` describes models only for x86 and s390x. aarch64/arm, RISC-V and MIPS print bare names,
PowerPC prints only a PVR, and many x86 entries are aliases. `CpuModelDescriptions.Enrich` fills the
gaps after parsing, in this order; **a description QEMU supplies is never replaced**:

1. `(alias of X)` / `(alias for X)` becomes `Alias of X: <X's description>`, resolved recursively
   (loop-safe).
2. x86 `(alias configured by machine type)` takes the description of its `<name>-v1` entry and
   adds that the exact version is set by the machine type (the QEMU CPU models page says the same).
3. Bare names come from hand-written tables: x86 (the 4 oldest), Arm, RISC-V, MIPS.
4. PowerPC is described by **rules over the family in the name** (G3/G4/G5, e300/e500/e600, MPC83xx/
   85xx/86xx, 4xx, POWERn, PPE42), plus the revision from the `_vX` suffix and QEMU's own PVR.
   Two-digit revisions are major.minor: `e500v2_v30` is 3.0, which the PVR's low byte confirms.
5. A model nothing matches keeps an empty description. A CPU added by a newer QEMU still appears,
   just without text, and is never given an invented one.

- **What is sourced and what is not.** The MIPS table follows
  <https://www.qemu.org/docs/master/system/qemu-cpu-models.html> (release level and year per core);
  checking it there caught `I7200`, which is **nanoMIPS**, not "MIPS32 Release 6". That page covers only
  x86 and MIPS, so **the Arm, RISC-V and PowerPC entries come from the vendors' own naming and have not
  been checked against QEMU documentation.** Keep them to the core and its architecture level.
- **`EveryModelOfARealQemu_HasADescription`** runs the real `-cpu help` output of QEMU 11.1 for all
  11 architectures (`tests/.../TestData/cpu-help/*.txt`) and fails naming any model left without text.
  It is what catches a typo in a table key. To cover a newer QEMU, refresh those fixtures from a
  real install; do not hand-edit them.
- Whitespace runs inside a description are collapsed: s390x pads its columns with spaces.
- `host` reads "Passes this computer's CPU to the guest. Recommended unless you need live migration",
  which is the page's own guidance for host passthrough.
- The dropdown truncates long text, so each item has a tooltip and the full description of the
  selected model is shown under the field.

## Disks and the Add-disk wizard

"Add disk..." in the editor's Storage tab opens `DiskWizardView` (three steps: source, details,
attach). It produces a `DiskDevice`; creating a new image runs `qemu-img create` first.

- **Sources:** create a new image, use an existing file, or attach over NBD, SSH or iSCSI.
- **`DiskFormatCatalog`** (Models) is the single description of every format: QEMU driver name,
  whether `qemu-img` can create it, whether QEMU can only read it, and the `-o` creation options.
  The option lists came from `qemu-img create -f <fmt> -o help` on QEMU 11.1. Only `bochs`, `cloop`
  and `dmg` truly cannot be created. `parallels` can (`qemu-img` supports it), but the app
  deliberately treats it, like the others, as attach-only and read-only.
- **Read-only formats are forced `readonly=on`** by the argument builder regardless of the flag.
- **`DiskFormat` is stored as an integer. Only ever append members** — reordering silently
  re-labels every saved disk. `ExistingMembers_KeepTheirStoredIntegerValues` pins the old six.
- **Connection data is stored twice on purpose, in two roles.** `DiskDevice.FilePath` holds the URI
  QEMU consumes (`nbd://`, `ssh://`, `iscsi://`, built by `DiskSourceUri`); `DiskDevice.ConnectionJson`
  holds the structured pieces (`DiskConnection`: host, port, export, user, remote path, target, LUN,
  key file) so they stay editable. The wizard writes both together; the builder reads `FilePath`.
- **Secrets never touch the database or the command line.** A LUKS passphrase is a *file path*
  in `ConnectionJson`; QEMU gets it through `-object secret,id=secN,file=...`. SSH uses the user's
  agent and keys. iSCSI CHAP passwords are not supported by the wizard (libiscsi reads
  `LIBISCSI_CHAP_PASSWORD` from the environment).
- **Commas in any value are doubled** (`,,`) in both `-drive` and `qemu-img -o`. Without it a path
  such as `a,readonly=on.img` would inject options.
- **`qemu-img` comes from the default installation's directory** (`DiskImageService.ResolveQemuImg`).
  With no default installation the wizard cannot create images and says so.
- `CreateImageAsync` refuses to overwrite an existing file; `qemu-img create` would do it silently.
- The wizard probes `qemu-img --help` for the block drivers the build has and **warns** (does not
  block) when NBD, SSH or iSCSI is missing. The dev machine's Windows QEMU 11.1 has no `iscsi`.
- `qemu-img info` without `-f` reports a **fixed VHD as `raw`**, because the VHD footer sits at the
  end. Creation is correct and the app always passes `format=` explicitly, but
  `DiskImageService.GetImageInfoAsync` probes, so it can misreport such a file.

## Application icon

The icon is "Lens + Cube": an amber magnifier with a cube inside, on the dark rounded tile of the
Control Room theme. Files live in `src/QemuXplorer.App/Assets/` and are **generated** by
`tools/build-icons.ps1` (Windows PowerShell, GDI+). Edit the shape table in that script and re-run
it; do not hand-edit the outputs.

```powershell
pwsh tools/build-icons.ps1
```

- One shape table renders `icon.svg`, `icon-512.png`, `icon-256.png`, `icon.ico` (16, 24, 32, 48,
  64, 128, 256 px) and `icon.icns` (16 to 1024 px, with the @2x sizes), all PNG-compressed, so the
  vector and the rasters cannot drift apart.
- **`icon.icns` is written by the script, not by `iconutil`**, so it can be regenerated on Windows.
  It uses the same type-to-size table `iconutil` emits (`icp4`, `ic11`, `icp5`, `ic12`, `ic07`,
  `ic13`, `ic08`, `ic14`, `ic09`, `ic10`). It has been validated structurally (`IconFilesTests`) but
  never opened on a Mac, so check it in Finder when the first macOS build exists.
- Draw the lens handle as a **flat-started line plus a separate round end**. A single round-capped
  line puts a cap wider than the ring at the junction, which shows as a bump at large sizes.
- **32 px and below use a simplified drawing of the same mark** (thicker lens, a cube of three flat
  faces, no thin edges). A 9-unit stroke is half a pixel at 16 px and smears into a blob. Raise
  `$smallMax` in the script if a size needs the other drawing.
- `QemuXplorer.App.csproj` sets `<ApplicationIcon>` to the `.ico` (the exe's icon, taskbar pins,
  Alt-Tab) and embeds `icon-256.png` as an `AvaloniaResource` for the windows.
- **Every `Window` must set `Icon="avares://QemuXplorer/Assets/icon-256.png"`.** A window without it
  shows the generic Avalonia glyph and nothing fails to say so; `AppIconTests` checks every `Window`
  subclass by reflection, and `IconFilesTests` checks the committed `.ico` and `.icns` structure.
- On macOS the Dock icon comes from the `.app` bundle, not from the window `Icon`: running with
  `dotnet run` shows the generic icon. See "macOS app bundle" below.

## macOS app bundle

`tools/build-macos-app.sh` assembles `QemuXplorer.app` from a self-contained publish:

```bash
dotnet publish src/QemuXplorer.App/QemuXplorer.App.csproj -c Release -r osx-arm64 --self-contained true -o publish/osx-arm64
bash tools/build-macos-app.sh publish/osx-arm64 dist --version 1.2.3
```

- **The layout** is `Contents/Info.plist`, `Contents/MacOS/` (the whole publish folder; the
  executable is `QemuXplorer`) and `Contents/Resources/icon.icns`. `CFBundleIconFile` is `icon`.
- **It runs on any OS with bash.** Assembly is only `cp` plus a heredoc, so it was developed and
  checked on Windows against a real `osx-arm64` publish: Mach-O executable, `Info.plist` parsed with
  `plistlib`, icon byte-identical to the repo's. **It has never been launched on a Mac.**
- **Signing needs `codesign`, which only exists on macOS.** Elsewhere the script builds the bundle
  and warns that it is unsigned. On a Mac it signs ad-hoc by default, because **Apple Silicon will not
  start an unsigned arm64 binary at all**; set `CODESIGN_IDENTITY` for a real Developer ID.
- **Notarization is not done.** Without it Gatekeeper blocks an app downloaded from the internet;
  locally, `xattr -dr com.apple.quarantine QemuXplorer.app` removes the block.
- `BUNDLE_ID` defaults to `io.github.yartat.qemuxplorer`; override it with the environment variable.
- `LSMinimumSystemVersion` is deliberately **not** set: the right floor depends on the .NET runtime
  that ships in the publish, and a wrong value would either block working systems or allow broken ones.
- `--version` goes straight into XML, so the script accepts only `1`, `1.2`, `1.2.3` or `1.2.3.4`.
- **CI:** the `macos` job builds both `osx-arm64` and `osx-x64`; see "CI" below.
- The publish is a plain self-contained folder of about 270 files, not a single file or trimmed.
  Single-file would need native-library extraction handling on macOS; trimming risks breaking
  Avalonia's reflection and EF Core. Neither was tried.

## Dependency injection

Composition root: `App.ConfigureServices` in `src/QemuXplorer.App/App.axaml.cs`.

The app resolves everything from the **root** provider — there are no DI scopes. A scoped
`DbContext` would therefore live for the whole process and be shared across threads, so the
context is registered as `AddDbContextFactory` instead and **every repository method creates
its own short-lived context**:

```csharp
await using var db = await _factory.CreateDbContextAsync(ct);
```

That makes repositories and services stateless, so they are registered as singletons. Page
view models are singletons too (the shell holds them for its lifetime); `VmEditViewModel` is
transient because it backs a dialog.

Reads use `AsNoTracking()` and hand back detached graphs. `VirtualMachineRepository.UpdateAsync`
therefore reloads the VM and reconciles children by key via `SyncChildren` — do not replace it
with `db.Update(graph)`, which marks not-yet-inserted children as Modified and throws.

- **A child new to the machine must be marked `EntityState.Added` explicitly.** Disks, adapters and USB
  devices get their `Guid` key on construction, so EF cannot tell "new" from "existing" by the key and
  would send an `UPDATE` that matches no row (`DbUpdateConcurrencyException`). `SyncChildren` does this;
  before, "Add disk" then Save on an existing machine threw.
- **The three collection `Include`s use `AsSplitQuery()`.** A single query multiplies rows
  (disks x adapters x USB devices) and EF logs `MultipleCollectionIncludeWarning`.
  `VirtualMachineRepositoryTests` runs on in-memory SQLite with that warning turned into an exception.

## Logging

Logs go to **a file, not the console**: `~/.qemu_explorer/logs/qemuxplorer-<yyyyMMdd>.log`, written by
Serilog (`AppLogging.Configure`). A desktop app has no console to read.

- Rolls daily, keeps 14 files, also rolls at 10 MB, and is opened `shared` so two running copies do
  not lock each other out. Every write is flushed, so a crash loses nothing.
- **Environment variables:** `QEMUXPLORER_LOG_CONSOLE=1` (or `true`/`yes`) adds the console, useful when
  running from a terminal; `QEMUXPLORER_LOG_LEVEL` is one of Trace, Debug, Information, Warning, Error,
  Critical (default Information). Unusable values are ignored rather than rejected.
- **EF Core is capped at Warning** unless the level is Debug or Trace, because it logs every SQL command
  at Information and would drown the file. At Debug, a run produces over a hundred EF lines.
- **Gotcha:** `AddSerilog` removes Microsoft.Extensions.Logging's level rules for its own provider, so
  `SetMinimumLevel` and `AddFilter` do **not** govern the file. The level and the EF cap are set inside
  Serilog; the MEL rules only govern the console. `AppLoggingTests` fails if this regresses.
- An unwritable log folder never stops the app: logging just goes nowhere.
- Code logs through `ILogger<T>`; there is no `Console.WriteLine` in the app. `Program.cs` still has
  Avalonia's `LogToTrace()`, which goes to `Trace` and is not captured in the file.
- **Unhandled exceptions are logged** by `UnhandledExceptionLogging`, installed first thing in `Program.cs`
  from `AppDomain.UnhandledException` (Critical when terminating), `TaskScheduler.UnobservedTaskException`
  (Error, marked observed) and, after Avalonia setup, `Dispatcher.UIThread.UnhandledException` (Critical).
  It **only records**: it never sets `Handled`, so the app still terminates as before. The logger is looked
  up per report because the container may not exist yet or may be disposed. If there is no logger or it
  throws, the entry goes to `~/.qemu_explorer/logs/unhandled.log`; the handler itself never throws.

## Process lifetime

`QemuProcessManager` is a singleton holding running processes in a
`ConcurrentDictionary<Guid, (Process, RunningVm)>` and raises `VmExited`.
`VirtualMachineService` re-exposes that event.

`VmExited` fires on a **thread-pool thread**. Any view model reacting to it must marshal via
`Dispatcher.UIThread.Post` before touching a bound collection — `DashboardViewModel` does.

`StopAsync` tries `CloseMainWindow()`, waits 3 seconds, then `Kill()`; `force: true` kills
immediately.

## UI conventions

- **The look is the "Control Room" design: dark, dense, keyboard-first.** All colour and font
  values live in `Theme/ControlRoom.axaml` as `App*` resources (`AppCanvas`, `AppRail`,
  `AppSurface`, `AppBorder`, `AppText`, `AppTextDim`, `AppAccent`, `AppOk`, `AppDanger`, …),
  defined twice under `ThemeDictionaries` for Dark and Light. **Never hard-code a colour in a
  view** — reference the token with `{DynamicResource AppX}`, and add a token in both variants
  if one is missing. Monospace uses `{DynamicResource AppMonoFont}`, a fallback chain rather
  than a vendored font.
- Reusable style classes, applied with `Classes="…"`: `pageTitle`, `section` (the small spaced
  capitals above a group), `mono`, `dim`, `nav` (rail items), `tool` (toolbar buttons),
  `primary` (the one amber button per screen), `danger`.
- The theme include must stay **last** in `App.axaml`'s `Styles`, after `FluentTheme` and the
  DataGrid theme, or Fluent's defaults win.
- Dark is the default: `App.axaml` sets `RequestedThemeVariant="Dark"`, the seed row is `Dark`,
  and `ApplyPersistedTheme` only switches to Light on an explicit stored "Light".
- MVVM via CommunityToolkit source generators: `[ObservableProperty]` on a `_camelCase` field,
  `[RelayCommand]` on a method. Bind to the generated `PascalCase` property and `XxxCommand`.
  ViewModels must be `partial` and derive from `ViewModelBase`.
- **Every code-behind must call `InitializeComponent()` in its constructor.** Omitting it
  compiles fine, raises no warning and throws nothing at runtime — the compiled XAML is simply
  never applied and the control renders as an empty box. `MainWindow` shipped as
  `public partial class MainWindow : Window { }` and the whole shell was blank.
  `ViewXamlLoadingTests` now guards this for every view.
- `AvaloniaUseCompiledBindingsByDefault` is `true`, so every `.axaml` with bindings needs an
  `x:DataType`. A missing one is a compile error, not a silent runtime failure.
- Navigation is view-model-first: `MainWindowViewModel.CurrentPage` is rendered by a
  `ContentControl`, resolved through the `DataTemplate`s in `App.axaml`.

  **Adding a page takes five edits**, and skipping any one fails silently rather than loudly:
  the ViewModel, the View, a `DataTemplate` in `App.axaml`, a DI registration in
  `App.ConfigureServices`, and a nav `Button` bound to a new `[RelayCommand]` in
  `MainWindowViewModel` / `MainWindow.axaml`.
- Pages load their data by overriding `OnAttachedToVisualTree` in code-behind and invoking
  `vm.LoadCommand` (`vm.RefreshCommand` on the dashboard) — there is no navigation-aware
  lifecycle, so **a new page without that override will render empty forever**.
- Entities are plain POCOs with no `INotifyPropertyChanged`. Mutating one already bound to a
  `DataGrid` will not update the UI — reload the `ObservableCollection` from the repository
  after a write instead. Do not add MVVM-toolkit attributes to `QemuXplorer.Models`.
- `VmEditView` is the exception to view-model-first: it is a modal dialog opened imperatively
  from `VirtualMachinesView.axaml.cs` using the `App.Services` service-locator, and closed via
  the ViewModel's `CloseRequested` event. It has no `DataTemplate`.
- The theme is applied at startup by `App.ApplyPersistedTheme` from the `Theme` app setting,
  and again by `SettingsViewModel` when the checkbox changes.
- The rail's active item comes from `MainWindowViewModel.IsDashboard` / `IsVirtualMachines` /
  `IsQemuInstallations` / `IsSettings`, recomputed in `OnCurrentPageChanged`. `CurrentPage`
  stays the single source of truth — do not add a parallel selected-index field.
- `VmEditViewModel` keeps the launch-command preview live by caching the default
  `QemuInstallation` in `LoadVmAsync` and rebuilding synchronously in `RefreshPreview` on every
  property change. Keep `RefreshPreview` free of I/O: it runs on each keystroke.

## Tests

FluentAssertions throughout, mirroring the source layout under
`tests/<Project>.Tests/<Folder>/<Type>Tests.cs`. Naming is
`Method_Scenario_ExpectedOutcome`, and the subject under test is a field named `_sut`.

**The two test projects are on different xUnit majors, and this is deliberate:**

| Project | xUnit | Covers |
|---|---|---|
| `QemuXplorer.Core.Tests` | v2 (`xunit` 2.9) | `QemuArgumentBuilder` |
| `QemuXplorer.App.Tests`  | v3 (`xunit.v3`)  | View XAML loading, headless |

`Avalonia.Headless.XUnit` depends on `xunit.v3.extensibility.core` 3.x, so the App tests cannot
use v2 **and `xunit.v3` must stay on `3.2.*`**. Bumping it to 4.x compiles and then fails every
test at discovery with `MissingMethodException` from `AvaloniaFactDiscoverer`. Do not "unify" the versions by downgrading the App tests — that breaks headless
testing. `dotnet test` on the solution runs both. A v3 project is a self-hosting executable,
hence its `<OutputType>Exe</OutputType>`.

### Headless UI tests

`ViewXamlLoadingTests` constructs every `ContentControl` in the App assembly — discovered by
reflection, so a new view is covered the moment it exists — and asserts `Content` is non-null.
That catches a view whose constructor never calls `InitializeComponent()`: the compiled XAML
is then never applied and the control renders as an empty box, with nothing failing at build
time. `MainWindow` shipped exactly that way and the entire shell was blank.

`TestAppBuilder` hosts a bare `HeadlessTestApp`, **not** QemuXplorer's `App`. The real one
builds the DI container and runs EF migrations against `~/.qemu_explorer` on startup, and a
test run must never touch the user's database. Keep it that way if you add fixtures.

Use `[AvaloniaFact]` / `[AvaloniaTheory]`, never plain `[Fact]`, for anything touching
Avalonia types — the Avalonia variants marshal the test onto the UI thread.

Two packages are wired up but unused, so reach for them rather than adding a dependency:

- **NSubstitute** is referenced by `Core.Tests` but no test uses it yet. Every collaborator
  behind the argument builder is already an interface (`IQemuProcessManager`,
  `IQemuArgumentBuilder`, the three repositories), so `VirtualMachineService` is testable today.
- **`Microsoft.EntityFrameworkCore.InMemory`** is declared in `Directory.Packages.props` but
  referenced by no project. Note it does not emulate SQLite's relational behaviour — prefer
  SQLite in-memory (`Data Source=:memory:`) for anything exercising the schema or migrations.

## CI and releases

`.github/workflows/dotnet-desktop.yml`:

- **Every push (any branch) and PR to main:** `build` fans out over windows/ubuntu/macos
  (`fail-fast: false`) with restore → build → test. Keep the steps plain `dotnet` invocations with
  `${{ }}` expressions; PowerShell `$env:` syntax is not the default shell on Linux and macOS runners.
- **Packaging** (`windows`, `linux`, `macos`) `needs: build`, so it runs only when tests passed on all
  three platforms, and uploads artifacts on every push. Version: the tag without `v` for a release,
  otherwise `1.0.<run number>`.
  - Windows x64 and x86: `QemuXplorer-<v>-win-<arch>-portable.exe` (single file, self-extracting
    natives, compressed), `-win-<arch>.zip` (self-contained folder) and `-win-<arch>-setup.exe`
    (NSIS, `tools/windows-installer.nsi`; x64 refuses to run on 32-bit Windows).
  - Linux amd64 and arm64: `qemuxplorer_<v>_<arch>.deb` from `tools/build-deb.sh`
    (`/opt/qemuxplorer`, `/usr/bin/qemuxplorer` symlink, desktop entry, icons).
  - macOS arm64 and x64: `QemuXplorer-<v>-<rid>.zip` containing the `.app` (unsigned except ad-hoc,
    not notarized; see "macOS app bundle").
- **Release:** pushing a tag matching `v<major>.<minor>.<patch>` runs the `release` job, which
  downloads every artifact and runs `gh release create` with generated notes. Only plain numeric
  tags trigger it, because the NSIS, deb and macOS version fields accept nothing else.

```bash
git tag v1.0.0
git push origin v1.0.0
```

- **Verified locally:** the single-file x64 portable exe starts and logs; the x64/x86 single-file
  publish works; `build-deb.sh` stages correctly (the `dpkg-deb` here is BusyBox and cannot build,
  and Git Bash cannot create symlinks). **Not run anywhere yet:** the NSIS installer, the real
  `dpkg-deb` step, the `release` job and the workflow as a whole.

## Known rough edges (mention, don't silently fix)

- `VmStatus` and `AcceleratorType` are dead enums — nothing reads or writes either. Accelerators
  are modelled as the four `bool` flags on `VirtualMachine` instead, and VM state is derived at
  runtime from `IQemuProcessManager.IsRunning`.
- `QemuDiscoveryService` only probes for `qemu-system-x86_64` in a fixed set of directories;
  other architectures and non-standard install paths are not detected.
- `DiskImageService.GetImageInfoAsync` is still not called from the UI (the wizard creates and
  attaches, but nothing inspects an existing image), and it probes the format, see above.
- `VirtualMachinesViewModel.AddVmAsync` persists a machine named "New machine" immediately
  instead of opening the editor first, and deleting a running machine does not stop it.
- `VirtualMachine.Disks`/`NetworkAdapters`/`UsbDevices` are `List<T>` on the entity but are
  copied into `ObservableCollection<T>` in the editor; the two are reconciled on save.
- `QemuProcessManager` tracks processes in memory only. Nothing survives an app restart, so a
  VM left running is orphaned and the dashboard will not show it again.
