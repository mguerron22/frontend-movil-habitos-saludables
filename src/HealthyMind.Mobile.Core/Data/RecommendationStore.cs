using HealthyMind.Mobile.Core.Models;
using SQLite;

namespace HealthyMind.Mobile.Core.Data;

/// <summary>Recomendación de la IA guardada en el teléfono para leerla sin conexión.</summary>
[Table("recommendations")]
public sealed class RecommendationRow
{
    [PrimaryKey]
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string? FocusHabit { get; set; }

    public string Recommendation { get; set; } = string.Empty;

    public string ModelName { get; set; } = string.Empty;

    [Indexed]
    public DateTimeOffset CreatedAt { get; set; }

    public static RecommendationRow From(RecommendationResponse response) => new()
    {
        Id = response.Id,
        Title = response.Title,
        Summary = response.Summary,
        FocusHabit = response.FocusHabit,
        Recommendation = response.Recommendation,
        ModelName = response.ModelName,
        CreatedAt = response.CreatedAt,
    };
}

public interface IRecommendationStore
{
    /// <summary>Más recientes primero.</summary>
    Task<IReadOnlyList<RecommendationRow>> GetAllAsync();

    Task SaveAsync(IEnumerable<RecommendationRow> recommendations);
}

internal sealed class RecommendationStore(LocalDatabase database) : IRecommendationStore
{
    public async Task<IReadOnlyList<RecommendationRow>> GetAllAsync()
    {
        var connection = await database.GetConnectionAsync();
        return await connection.Table<RecommendationRow>().OrderByDescending(row => row.CreatedAt).ToListAsync();
    }

    public async Task SaveAsync(IEnumerable<RecommendationRow> recommendations)
    {
        var connection = await database.GetConnectionAsync();
        foreach (var recommendation in recommendations)
        {
            await connection.InsertOrReplaceAsync(recommendation);
        }
    }
}
