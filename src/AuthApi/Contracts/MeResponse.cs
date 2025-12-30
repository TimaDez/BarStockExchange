namespace AuthApi.Contracts;

public sealed record MeResponse(Guid UserId, string Email, string Role, Guid PubId);
