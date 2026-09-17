/* 
 Description: This file defines the MachineType enum, which represents different machine types supported by QEMU. Each enum member is decorated with a Display attribute that provides a user-friendly name and description for the machine type. The GroupName property in the Display attribute categorizes the machine types based on their architecture (e.g., x86_64, Aarch64).
 Used links: https://doc.opensuse.org/documentation/leap/archive/42.1/virtualization/html/book.virt/cha.qemu.running.html
             https://computernewb.com/wiki/QEMU/Devices/Machines#i386/x86_64
*/

using System.ComponentModel.DataAnnotations;

namespace QemuXplorer.Models.Enums;

public enum MachineType
{
    [Display(Name = "PC", Description = "Standard PC (i440FX + PIIX, 1996)",GroupName = "x86_64")]
    pc,
    [Display(Name = "PC-Q35", Description = "Standard PC (Q35 + ICH9, 2009)",GroupName = "x86_64")]
    q35,
    [Display(Name = "MICROVM", Description = "Minimalist PC which does not have a PCI bus or ACPI",GroupName = "x86_64")]
    microvm,
    [Display(Name = "ISAPC", Description = "\"Retro\" style PC which does not have a PCI bus",GroupName = "x86_64")]
    isapc,
    [Display(Name = "None", Description = "None",GroupName = "x86_64")]
    none,
    [Display(Name = "XenFV", Description = "Xen Fully-virtualized PC",GroupName = "x86_64")]
    xenfv,
    [Display(Name = "XENPV", Description = "Xen Para-virtualized PC",GroupName = "x86_64")]
    xenpv,
    [Display(Name = "AKITA", Description = "Sharp Zaurus SL-C1000 PDA (PXA270)", GroupName = "Aarch64")]
    akita,
    [Display(Name = "AST2500-EVB", Description = "ASPEED AST2500 EVB (ARM1176)", GroupName = "Aarch64")]
    ast2500_evb,
    [Display(Name = "AST2600-EVB", Description = "ASPEED AST2600 EVB (ARM1176)", GroupName = "Aarch64")]
    ast2600_evb,
    [Display(Name = "BOROZI", Description = "Sharp Zaurus SL-C3100 PDA (S3C2410A, ARM920T)", GroupName = "Aarch64")]
    borozi,
    [Display(Name = "CANON-A1100", Description = "Canon PowerShot A1100 IS", GroupName = "Aarch64")]
    canon_a1100,
    [Display(Name = "CHEETAH", Description = "Palm Tungsten E (OMAP310)", GroupName = "Aarch64")]
    cheetah,
    [Display(Name = "VIRT", Description = "Virtual Machine")]
    virt,
    [Display(Name = "RASPI", Description = "Raspberry Pi")]
    raspi,
    [Display(Name = "VERSATILE", Description = "Versatile PB")]
    versatile,
    [Display(Name = "SBSA-REF", Description = "ARM64 with GICv3 interrupt controllers, AHCI and XHCI controllers")]
    sbsa_ref,
    [Display(Name = "SPIKE", Description = "RISC-V Spike")]
    spike,
    sifive_u,
    pseries,
    s390_ccw_virtio,
    malta,
    mpc85xx,
    mpc86xx,
    mpc8xx,
    mpc83xx,
    mpc74xx,
    mpc82xx,
    mpc85xx_p5020,
    mpc85xx_p5021,
    mips,
    mips64,
    mipsel,
    mips64el,
    mips64el_p5020,
    sun4m,
    sun4u,
    niagara,
    niagara2,
    niagara3,
    niagara4,
    ppc,
    ppc64,
    ppc64le,
    ppc64le2,
    _40p,
    bamboo,
    g3beige,
    mac99,
    mpc8544ds,
    pegasos2,
    powernv10,
    powernv8,
    powernv9,
    powernv,
    ppce500,
    pseries_2_1,
    pseries_2_2,
    pseries_2_3,
    pseries_2_4,
    pseries_2_5,
    pseries_2_6,
    pseries_2_7,
    pseries_2_8,
    pseries_2_9,
    pseries_2_10,
    pseries_2_11,
    pseries_2_12,
    pseries_2_12_sxxm,
    pseries_3_0,
    pseries_3_1,
    pseries_4_0,
    pseries_4_1,
    pseries_5_0,
    pseries_5_1,
    pseries_6_0,
    pseries_6_1,
    pseries_6_2,
    ref405ep,
    sam460ex,
    taihu,
    virtex_ml507,
};
