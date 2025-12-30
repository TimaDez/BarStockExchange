namespace AuthApi.Models;

public sealed class Pub
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<UserPub> UserPubs { get; set; } = new();
}
