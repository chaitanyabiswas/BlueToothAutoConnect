using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueToothAutoConnect.Persistence.Entities;

[Table("DeviceHistory")]
public class DeviceHistory
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(17)]
    public string DeviceAddress { get; set; } = string.Empty;

    [Required]
    [MaxLength(512)]
    public string DeviceId { get; set; } = string.Empty;

    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    public DateTime? LastConnectedAt { get; set; }

    [MaxLength(512)]
    public string? ReplacedByDeviceId { get; set; }

    public DateTime? ReplacedAt { get; set; }
}