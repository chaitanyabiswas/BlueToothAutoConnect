using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueToothAutoConnect.Persistence.Entities;

[Table("WhitelistEntries")]
public class WhitelistEntry
{
    [Key]
    public int Id { get; set; }

    [MaxLength(64)]
    public string? DeviceAddress { get; set; }

    [MaxLength(256)]
    public string? DeviceName { get; set; }

    [MaxLength(128)]
    public string? Category { get; set; }

    [MaxLength(256)]
    public string FriendlyName { get; set; } = string.Empty;

    public bool AddedByUser { get; set; } = true;

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(256)]
    public string ProfileFlags { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}