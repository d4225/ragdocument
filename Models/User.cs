using System.ComponentModel.DataAnnotations;

namespace SmartDocumentRAG.API.Models;

public class User
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Role { get; set; } = "User"; // Roles: "Admin", "Lecturer", "User"

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Quan hệ 1-N: 1 User có thể upload nhiều Document
    public ICollection<Document> Documents { get; set; } = new List<Document>();
}