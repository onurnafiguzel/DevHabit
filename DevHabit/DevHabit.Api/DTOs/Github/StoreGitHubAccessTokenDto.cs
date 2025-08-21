namespace DevHabit.Api.DTOs.Github;

public sealed record StoreGitHubAccessTokenDto
{
    public required string AccessToken { get; init; }
    public required int ExpiresInDays { get; init; }
}
