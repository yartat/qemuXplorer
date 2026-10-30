namespace QemuXplorer.Models.Enums;

/// <summary>
/// Stored as an integer, so new members are appended: reordering would silently re-label every
/// disk already saved in a user's database.
/// </summary>
public enum DiskFormat
{
    QCow2,
    Raw,
    Vmdk,
    Vhd,
    Vhdx,
    Vdi,
    Qcow,
    Qed,
    Luks,
    Bochs,
    Cloop,
    Dmg,
    Parallels
}
