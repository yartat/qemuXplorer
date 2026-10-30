# Release notes

The newest version comes first. The GitHub release for a tag `vX.Y.Z` takes its text from the
`## X.Y.Z` section of this file, and the release workflow fails if that section is missing or empty.

**Keeping it up to date:** describe every user-visible change under `## Unreleased` as you make it.
Before tagging, rename that heading to the version and date (`## 1.1.0 (2026-12-01)`) and add a fresh
`## Unreleased` above it. See [BUILD.md](BUILD.md#making-a-release).

## Unreleased

## 1.1.0 (2026-10-30)

The Storage, Network and USB tabs now edit what is in them by its type.

### Added
- **Edit disk.** "Edit..." (or a double-click) on a disk in a machine's Storage tab opens a dialog. It changes how
  the disk attaches and, for local image files, changes the image through `qemu-img`: resize, convert to another
  format, change or remove the backing file, change qcow2 settings, and check or repair. Each format shows only what
  it supports. The exact commands are shown for confirmation before they run.
- **Network adapters.** "Add adapter..." opens a wizard and "Edit..." changes an adapter according to its type: NAT
  with port forwarding, subnet, DHCP, DNS, host name and isolation; tap; bridge; and sockets (listen, connect,
  multicast, UDP). Two more adapter models, vmxnet3 and pcnet. A MAC address that does not clash with the machine's
  other adapters is filled in for you.
- **USB devices.** "Add device..." opens a wizard and "Edit..." changes a device according to its type: a device from
  this computer (picked from a list of what is plugged in, by vendor and product or by bus and address), a USB stick
  from an image file, tablet, mouse, keyboard, audio, serial port, smart card reader, and the USB controller (USB 3,
  2 or 1.1).
- The adapter model, audio driver, USB controller and a USB stick's image format each show a description of the
  selected choice under the field.
- In the Add adapter and Add USB device wizards, a double-click on a type does what Next does.

### Fixed
- Add-disk wizard on Windows: qcow2 "falloc" and "full" preallocation failed with "Unsupported preallocation mode"
  because QEMU on Windows cannot do them. They are no longer offered there (off and metadata remain).
- A failed image creation no longer leaves a partial file behind that blocked the next attempt with "already exists".

### Upgrading
- The first start of this version upgrades your database automatically. Adapters and USB devices you already have
  keep working, and a saved USB entry opens as a device from this computer.

### Known limitations
- Disks reached over the network (NBD, SSH, iSCSI) and encrypted (LUKS) images offer only how they attach in the Edit
  dialog; their image is not changed from there.
- A USB stick takes an existing image file; create one with the Add disk wizard first.
- Multicast sockets do not work in QEMU on Windows, and the Windows build of QEMU has no bridge networking. The
  dialogs warn about both.
- A smart card reader waits for another program (such as vscclient) to supply the card on a TCP port.
- The list of USB devices on macOS has not been tried on a real Mac, and macOS does not report bus and address.
- Windows builds and the macOS app are not code-signed, and the macOS app is not notarized, so Windows SmartScreen and
  macOS Gatekeeper warn on first start. The macOS app has not been launched on a real Mac yet.
- Only `qemu-system-x86_64` is auto-detected; other architectures can be added by hand.
- A virtual machine left running when the app closes is not shown again after a restart.

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
