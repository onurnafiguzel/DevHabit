using DevHabit.Api.DTOs.Github;
using Refit;

namespace DevHabit.Api.Services;

public sealed class RefitGitHubService(IGithubApi githubApi, ILogger<GitHubService> logger)
{
    public async Task<GitHubUserProfileDto?> GetUserProfileAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(accessToken);

        ApiResponse<GitHubUserProfileDto> response = await githubApi.GetUserProfile(accessToken, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Failed to get Github user profile. Statsu code: {StatusCode}", response.StatusCode);
            return null;
        }

        return response.Content;
    }

    public async Task<IReadOnlyList<GitHubEventDto>?> GetUserEventsAsync(
        string username,
        string accessToken,
        int page = 1,
        int perPage = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(accessToken);
        ArgumentException.ThrowIfNullOrEmpty(username);

        ApiResponse<List<GitHubEventDto>> response = await githubApi.GetUserEvents(
            username,
            accessToken,
            page,
            perPage,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Failed to get Github user events. Status code: {StatusCode}", response.StatusCode);
            return null;
        }

        return response.Content;
    }
}
