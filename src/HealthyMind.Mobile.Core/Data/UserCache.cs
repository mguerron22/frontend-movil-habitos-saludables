using System.Text.Json;
using HealthyMind.Mobile.Core.Api;
using HealthyMind.Mobile.Core.Models;

namespace HealthyMind.Mobile.Core.Data;

public interface IUserCache
{
    Task<UserProfile?> GetAsync();

    Task SaveAsync(UserProfile user);
}

internal sealed class UserCache(LocalDatabase database, TimeProvider timeProvider) : IUserCache
{
    public async Task<UserProfile?> GetAsync()
    {
        var connection = await database.GetConnectionAsync();
        var row = await connection.FindAsync<CachedUserRow>(1);
        return row is null ? null : JsonSerializer.Deserialize<UserProfile>(row.Json, ApiClient.JsonOptions);
    }

    public async Task SaveAsync(UserProfile user)
    {
        var connection = await database.GetConnectionAsync();
        await connection.InsertOrReplaceAsync(new CachedUserRow
        {
            Json = JsonSerializer.Serialize(user, ApiClient.JsonOptions),
            SavedAt = timeProvider.GetUtcNow(),
        });
    }
}
