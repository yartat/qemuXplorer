# Release notes

The newest version comes first. The GitHub release for a tag `vX.Y.Z` takes its text from the
`## X.Y.Z` section of this file, and the release workflow fails if that section is missing or empty.

**Keeping it up to date:** describe every user-visible change under `## Unreleased` as you make it.
Before tagging, rename that heading to the version and date (`## 1.1.0 (2026-12-01)`) and add a fresh
`## Unreleased` above it. See [BUILD.md](BUILD.md#making-a-release).

## Unreleased

## 1.0.0 (2026-10-30)

First public release.

### Features
- Define QEMU virtual machines in a UI and keep them in a local SQLite database.
- Preview the exact `qemu-system-*` command line before launching, then start and stop the machine.
- Guest architectures: x86_64, i386, ARM/AArch64, RISC-V, PowerPC, MIPS, s390x and more.
- Machine types are listed per architecture, so the picker only offers machines the emulator can run.
- CPU model picker filled from the installed QEMU (`-cpu help`), with a description for each model.
  `host` is the default only when the computer's CPU matches the guest architecture; otherwise the
  machine's own default CPU is used.
- Add-disk wizard: create qcow2, qcow, qed, vdi, vmdk, vpc, vhdx and raw images with `qemu-img`, attach
  existing files (including read-only formats), or connect over NBD, SSH or iSCSI. LUKS encryption is
  supported through a passphrase file.
- Accelerator selection (KVM, HVF, WHPX) with TCG fallback; QEMU installations are detected automatically.
- Dark "Control Room" theme with a light variant.

### Logging and diagnostics
- Logs are written to `~/.qemu_explorer/logs`, not the console. Unhandled errors are recorded there too,
  or in `unhandled.log` if the logger itself is unavailable.

### Downloads
- Windows (x64 and x86): installer, zip and a single-file portable exe.
- Linux (amd64 and arm64): `.deb` package.
- macOS (Apple Silicon and Intel): `QemuXplorer.app` in a zip.

The builds include the .NET runtime. QEMU is not included; install it separately.
