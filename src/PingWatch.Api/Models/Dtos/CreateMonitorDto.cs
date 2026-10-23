using System.ComponentModel.DataAnnotations;

namespace PingWatch.Api.Models.Dtos;

public class CreateMonitorDto
{
    [Required]
    [StringLength(200, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Url]
    [StringLength(2000)]
    public string Url { get; set; } = string.Empty;
}
