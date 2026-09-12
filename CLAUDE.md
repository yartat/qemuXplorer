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
solution runs both test projects: 24 argument-builder tests and 8 headless UI tests.

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
  bottom of the file (`FormatName`, `InterfaceName`, `CacheName`, …). Adding an enum member
  means adding a case there — the `_ =>` fallbacks will otherwise silently pick a default.
- QEMU driver names are not the enum names. `DiskFormat.Vhd` is `vpc` on the command line and
  in `qemu-img` output; `DiskImageService` must agree with `FormatName` on this.
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

`Avalonia.Headless.XUnit` depends on `xunit.v3.extensibility.core`, so the App tests cannot
use v2. Do not "unify" the versions by downgrading the App tests — that breaks headless
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

## CI

`.github/workflows/dotnet-desktop.yml` runs a real `matrix.os` fan-out across
windows/ubuntu/macos with `fail-fast: false`, then restore → build → test. Keep the steps
plain `dotnet` invocations with `${{ }}` expressions: the previous version used PowerShell
`$env:` syntax, which is not the default shell on the Linux and macOS runners.

## Known rough edges (mention, don't silently fix)

- `VmStatus` and `AcceleratorType` are dead enums — nothing reads or writes either. Accelerators
  are modelled as the four `bool` flags on `VirtualMachine` instead, and VM state is derived at
  runtime from `IQemuProcessManager.IsRunning`.
- `QemuDiscoveryService` only probes for `qemu-system-x86_64` in a fixed set of directories;
  other architectures and non-standard install paths are not detected.
- `DiskImageService` is registered in DI but injected nowhere: no view model calls it, so
  creating and inspecting disk images is not reachable from the UI. It also takes the
  `qemu-img` path as a parameter, and nothing in the app resolves that path — discovery only
  looks for `qemu-system-x86_64`.
- `VirtualMachinesViewModel.AddVmAsync` persists a machine named "New machine" immediately
  instead of opening the editor first, and deleting a running machine does not stop it.
- `VirtualMachine.Disks`/`NetworkAdapters`/`UsbDevices` are `List<T>` on the entity but are
  copied into `ObservableCollection<T>` in the editor; the two are reconciled on save.
- `QemuProcessManager` tracks processes in memory only. Nothing survives an app restart, so a
  VM left running is orphaned and the dashboard will not show it again.
