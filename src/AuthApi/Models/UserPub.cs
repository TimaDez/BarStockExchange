namespace AuthApi.Models;

public sealed class UserPub
{
    public Guid UserId { get; set; }
    public AuthUser User { get; set; } = null!;

    public Guid PubId { get; set; }
    public Pub Pub { get; set; } = null!;

    public UserRole Role { get; set; }
}
