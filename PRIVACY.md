# Privacy

QEMU Explorer runs entirely on your computer. It has no accounts, no analytics, no crash reporting
and no update check, and it does not contact any server of its own.

## What it stores, and where

Everything is kept in `~/.qemu_explorer` (on Windows, `C:\Users\<you>\.qemu_explorer`):

| What | Where | Contents |
|---|---|---|
| Database | `data.db` | Your virtual machine definitions, the QEMU installations it found, and settings such as the theme |
| Logs | `logs/qemuxplorer-<date>.log` | Start-up, the ids and exit codes of machines it started, warnings and errors, and unhandled exceptions with their stack traces |
| Crash fallback | `logs/unhandled.log` | Only written if an error happens when the logger cannot be used |

Daily log files are kept (the last 14) and are never uploaded. They can contain file paths and error messages
from QEMU tools, so look through one before you attach it to a bug report.

Nothing is stored outside this folder, apart from what you choose: disk images are created where you
tell the wizard to put them.

## What it runs and connects to

- It starts the `qemu-system-*` and `qemu-img` programs you have installed, with the arguments shown in
  the command preview. QEMU is a separate program with its own behaviour and its own privacy properties.
- When you edit a disk, `qemu-img` changes the image file you chose, using the commands the dialog shows first.
- To let you pick a USB device to pass through, it lists the USB devices plugged into this computer: it reads
  `/sys/bus/usb` on Linux, asks PowerShell for the USB entries of the Windows device list, or runs `system_profiler`
  on macOS. The list stays on this computer; only the vendor and product ids you pick are saved with the machine.
- Network access happens only if you configure it: a disk attached over NBD, SSH or iSCSI makes QEMU
  connect to the host you typed, and a virtual machine's own network adapters carry that machine's traffic.
- Secrets are not stored. A disk-encryption passphrase is a path to a file you own; the app saves the
  path, never the passphrase. SSH uses your own agent and keys.

## Deleting your data

Close the app and delete the `~/.qemu_explorer` folder. Uninstalling the program does not remove it.
Disk images you created are separate files and are not touched.

## Changes

If a later version adds anything that sends data off your computer, it will be listed in the
[release notes](docs/RELEASE-NOTES.md) and described here first.
