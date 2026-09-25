using AIChallenge.McpScheduler;
using Microsoft.Extensions.Configuration;

namespace AIChallenge.McpPipeline.Tools;

/// <summary>
/// Конфигурация LLM-сервиса.
/// </summary>
public record LlmConfig(
    string ClientId,
    string ClientSecret,
    string Model,
    double Temperature,
    int MaxTokens
);

/// <summary>
/// Хелпер для парсинга LLM-конфигурации из IConfiguration.
/// </summary>
public static class LlmConfigHelper
{
    public static bool TryParse(IConfiguration configuration, Action<string> log, out LlmConfig? config, out McpScheduler.LlmSummaryService? llmService)
    {
        config = null;
        llmService = null;

        var gigaSection = configuration.GetSection("GigaChat");
        if (!gigaSection.Exists())
        {
            return false;
        }

        var clientId = gigaSection["ClientId"];
        var clientSecret = gigaSection["ClientSecret"];
        var model = gigaSection["Model"] ?? "GigaChat-2";
        var tempStr = gigaSection["Temperature"];
        var temperature = !string.IsNullOrEmpty(tempStr) && double.TryParse(tempStr, out var parsedTemp) ? parsedTemp : 0.3;
        var maxTokens = 2000;
        if (int.TryParse(gigaSection["MaxTokens"], out var parsedMt))
            maxTokens = parsedMt;

        config = new LlmConfig(clientId!, clientSecret!, model, temperature, maxTokens);
        llmService = new McpScheduler.LlmSummaryService(config.ClientId, config.ClientSecret, config.Model, config.Temperature, config.MaxTokens, log);

        return true;
    }
}
