namespace QemuXplorer.Models.Enums;

/// <summary>Where a disk's bytes live, as far as QEMU is concerned.</summary>
public enum DiskSourceKind
{
    LocalFile,
    Nbd,
    Ssh,
    Iscsi
}
