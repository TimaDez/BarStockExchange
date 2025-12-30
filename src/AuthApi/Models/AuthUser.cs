namespace AuthApi.Models;

public class AuthUser
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    public ICollection<UserPub> UserPubs { get; set; } = new List<UserPub>();
}