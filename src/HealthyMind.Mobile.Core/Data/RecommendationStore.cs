using HealthyMind.Mobile.Core.Models;
using SQLite;

namespace HealthyMind.Mobile.Core.Data;

/// <summary>Recomendación de la IA guardada en el teléfono para leerla sin conexión.</summary>
[Table("recommendations")]
public sealed class RecommendationRow
{
    /// <summary>Así llama el servidor a las recomendaciones armadas con reglas cuando la IA no respondió.</summary>
    public const string RuleBasedModel = "reglas-healthymind";

    [PrimaryKey]
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string? FocusHabit { get; set; }

    public string Recommendation { get; set; } = string.Empty;

    public string ModelName { get; set; } = string.Empty;

    [Indexed]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>"Me sirvió" (true) / "No me sirvió" (false) / sin opinión (null).</summary>
    public bool? WasHelpful { get; set; }

    /// <summary>La opinión todavía no llegó al servidor.</summary>
    public bool FeedbackPending { get; set; }

    [Ignore]
    public bool IsRuleBased => ModelName == RuleBasedModel;

    public static RecommendationRow From(RecommendationResponse response) => new()
    {
        Id = response.Id,
        Title = response.Title,
        Summary = response.Summary,
        FocusHabit = response.FocusHabit,
        Recommendation = response.Recommendation,
        ModelName = response.ModelName,
        CreatedAt = response.CreatedAt,
        WasHelpful = response.WasHelpful,
    };
}

public interface IRecommendationStore
{
    /// <summary>Más recientes primero.</summary>
    Task<IReadOnlyList<RecommendationRow>> GetAllAsync();

    /// <summary>Lo que llega del servidor. No pisa una opinión que todavía no se envió.</summary>
    Task SaveAsync(IEnumerable<RecommendationRow> recommendations);

    Task SetFeedbackAsync(Guid id, bool helpful);

    Task<IReadOnlyList<RecommendationRow>> GetPendingFeedbackAsync();

    Task MarkFeedbackSentAsync(Guid id);
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
            var local = await connection.FindAsync<RecommendationRow>(recommendation.Id);
            if (local is { FeedbackPending: true })
            {
                recommendation.WasHelpful = local.WasHelpful;
                recommendation.FeedbackPending = true;
            }

            await connection.InsertOrReplaceAsync(recommendation);
        }
    }

    public async Task SetFeedbackAsync(Guid id, bool helpful)
    {
        var connection = await database.GetConnectionAsync();
        await connection.ExecuteAsync(
            "UPDATE recommendations SET WasHelpful = ?, FeedbackPending = 1 WHERE Id = ?", helpful, id);
    }

    public async Task<IReadOnlyList<RecommendationRow>> GetPendingFeedbackAsync()
    {
        var connection = await database.GetConnectionAsync();
        return await connection.Table<RecommendationRow>().Where(row => row.FeedbackPending).ToListAsync();
    }

    public async Task MarkFeedbackSentAsync(Guid id)
    {
        var connection = await database.GetConnectionAsync();
        await connection.ExecuteAsync("UPDATE recommendations SET FeedbackPending = 0 WHERE Id = ?", id);
    }
}
