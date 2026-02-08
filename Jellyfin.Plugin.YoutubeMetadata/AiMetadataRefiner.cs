using Jellyfin.Plugin.YoutubeMetadata.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.YoutubeMetadata
{
    internal static class AiMetadataRefiner
    {
        public static async Task<(string Title, string Description)?> TryRefineAsync(
            string title,
            string description,
            PluginConfiguration config,
            HttpClient httpClient,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            if (!config.EnableAiMetadataCleanup)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(config.AiApiKey) || string.IsNullOrWhiteSpace(config.AiModel))
            {
                return null;
            }

            try
            {
                var endpoint = BuildEndpoint(config.AiBaseUrl);
                var payload = BuildRequestPayload(config.AiModel, title, description, config.EnableAiDescriptionCleanup);
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.AiApiKey);
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("AI cleanup failed with status code {StatusCode}", (int)response.StatusCode);
                    return null;
                }

                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var result = ParseRefinedPayload(responseContent);
                if (result is null)
                {
                    logger.LogWarning("AI cleanup returned no usable content");
                    return null;
                }

                return result;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "AI cleanup failed unexpectedly");
                return null;
            }
        }

        private static string BuildEndpoint(string baseUrl)
        {
            var sanitized = string.IsNullOrWhiteSpace(baseUrl)
                ? "https://api.openai.com/v1"
                : baseUrl.Trim().TrimEnd('/');
            return sanitized + "/chat/completions";
        }

        private static string BuildRequestPayload(string model, string title, string description, bool refineDescription)
        {
            var systemPrompt = refineDescription
                ? "Normalize YouTube metadata. Return compact JSON with keys title and description."
                : "Normalize YouTube metadata title. Return compact JSON with keys title and description.";
            var userPrompt = "Title:\n" + (title ?? string.Empty) +
                "\n\nDescription:\n" + (description ?? string.Empty) +
                "\n\nReturn only JSON.";

            var request = new ChatCompletionRequest
            {
                Model = model,
                Temperature = 0.2,
                Messages = new[]
                {
                    new ChatMessage { Role = "system", Content = systemPrompt },
                    new ChatMessage { Role = "user", Content = userPrompt }
                }
            };

            return JsonSerializer.Serialize(request);
        }

        private static (string Title, string Description)? ParseRefinedPayload(string responseContent)
        {
            using var root = JsonDocument.Parse(responseContent);
            if (!root.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            {
                return null;
            }

            var messageContent = choices[0].GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(messageContent))
            {
                return null;
            }

            var cleanJson = ExtractJsonObject(messageContent);
            if (string.IsNullOrWhiteSpace(cleanJson))
            {
                return null;
            }

            using var parsed = JsonDocument.Parse(cleanJson);
            var title = parsed.RootElement.TryGetProperty("title", out var titleElement)
                ? titleElement.GetString() ?? string.Empty
                : string.Empty;
            var description = parsed.RootElement.TryGetProperty("description", out var descriptionElement)
                ? descriptionElement.GetString() ?? string.Empty
                : string.Empty;

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(description))
            {
                return null;
            }

            return (title, description);
        }

        private static string ExtractJsonObject(string text)
        {
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                return string.Empty;
            }

            return text.Substring(start, (end - start) + 1);
        }

        private sealed class ChatCompletionRequest
        {
            [JsonPropertyName("model")]
            public string Model { get; set; }

            [JsonPropertyName("temperature")]
            public double Temperature { get; set; }

            [JsonPropertyName("messages")]
            public ChatMessage[] Messages { get; set; }
        }

        private sealed class ChatMessage
        {
            [JsonPropertyName("role")]
            public string Role { get; set; }

            [JsonPropertyName("content")]
            public string Content { get; set; }
        }
    }
}
