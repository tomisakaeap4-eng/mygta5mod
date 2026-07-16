using OpenAI;
using OpenAI.Chat;
using System;
using System.ClientModel;

namespace FirstLegacyMod
{
    /// <summary>
    /// Lightweight AI communication service for chat bubble responses.
    /// Sends a prompt to the OpenAI-compatible API and returns a random
    /// Vietnamese response.
    ///
    /// Uses <see cref="ChatClient"/> with hardcoded API key + local base URL
    /// for local dev mod. See <c>ApiKey</c>, <c>BaseUrl</c>, <c>Model</c> constants.
    /// </summary>
    public static class AIChatService
    {
        // ── System prompt: ask AI to respond in Vietnamese with random fun replies ──
        private const string SystemPrompt =
            "Bạn là một trợ lý AI vui tính trong game GTA V. " +
            "Hãy luôn trả lời bằng tiếng Việt với một câu ngẫu nhiên, " +
            "thú vị, hài hước và NGẮN GỌN (tối đa 1-2 câu, dưới 200 ký tự). " +
            "Có thể là bình luận về game, lời khuyên hài hước, câu đùa, " +
            "hoặc phản ứng bất ngờ. Không lặp lại câu trước đó.";

        private const string UserPrompt =
            "Cho tôi một câu trả lời ngẫu nhiên bằng tiếng Việt.";

        // ── Configuration ────────────────────────────────────────────
        // NOTE: API key hardcoded for local dev mod.
        // For production, use Environment.GetEnvironmentVariable("OPENAI_API_KEY").
        private const string ApiKey = "sk-95a1ecf324105598-e8n9c5-8221cc03";
        private const string BaseUrl = "http://localhost:20128/v1";
        private const string Model = "oc/deepseek-v4-flash-free(max)";

        // ── Instance state ────────────────────────────────────────────
        private static readonly object _lock = new object();
        private static ChatClient _client;
        private static bool _initialized;
        private static string _lastResponse = string.Empty;

        /// <summary>
        /// Whether the service has been successfully initialized with an API key.
        /// </summary>
        public static bool IsAvailable
        {
            get
            {
                EnsureInitialized();
                return _client != null;
            }
        }

        // ── Public API ────────────────────────────────────────────────

        /// <summary>
        /// Initialize the ChatClient with hardcoded API key + local base URL.
        /// Safe to call multiple times.
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;

            lock (_lock)
            {
                if (_initialized) return;

                _client = new ChatClient(
                    model: Model,
                    credential: new ApiKeyCredential(ApiKey),
                    options: new OpenAIClientOptions
                    {
                        Endpoint = new Uri(BaseUrl)
                    });

                _initialized = true;
            }
        }

        /// <summary>
        /// Sends a request to the AI and returns a random Vietnamese response.
        /// This method is synchronous and will block the calling thread.
        /// Call from a background thread via <c>Task.Run</c>.
        /// </summary>
        /// <returns>The AI response text, or an error message.</returns>
        public static string GetVietnameseResponse()
        {
            return GetSituationalResponse(SystemPrompt, UserPrompt);
        }

        /// <summary>
        /// Sends a request to the AI with a custom situation-specific prompt.
        /// This method is synchronous and will block the calling thread.
        /// Call from a background thread via <c>Task.Run</c>.
        /// </summary>
        /// <param name="systemPrompt">
        /// Custom system prompt describing the NPC's role and the situation.
        /// </param>
        /// <param name="userPrompt">
        /// Custom user prompt describing what the NPC should react to.
        /// </param>
        /// <returns>The AI response text, or an error message.</returns>
        public static string GetSituationalResponse(
            string systemPrompt, string userPrompt)
        {
            EnsureInitialized();

            if (_client == null)
            {
                return "[AI] Chưa khởi tạo AIChatService. Kiểm tra kết nối localhost:20128.";
            }

            try
            {
                ChatMessage[] messages =
                {
                    new SystemChatMessage(systemPrompt),
                    new UserChatMessage(userPrompt),
                };

                ChatCompletionOptions options = new ChatCompletionOptions
                {
                    MaxOutputTokenCount = 150,
                    Temperature = 1.2f, // High randomness for varied responses
                };

                ChatCompletion completion = _client.CompleteChat(messages, options);

                string response = completion.Content[0]?.Text?.Trim() ?? string.Empty;

                // If response is same as last, retry once with higher temperature.
                // NOTE: This incurs an extra API call; acceptable for a game mod
                // where variety matters more than cost.
                if (!string.IsNullOrEmpty(response) &&
                    response == _lastResponse)
                {
                    options.Temperature = 1.5f;
                    completion = _client.CompleteChat(messages, options);
                    response = completion.Content[0]?.Text?.Trim() ?? string.Empty;
                }

                _lastResponse = response;
                return string.IsNullOrEmpty(response)
                    ? "[AI] Không nhận được phản hồi. Thử lại sau."
                    : response;
            }
            catch (Exception ex)
            {
                return $"[AI Lỗi] {ex.Message}";
            }
        }

        // ── Internal ──────────────────────────────────────────────────

        private static void EnsureInitialized()
        {
            if (!_initialized)
            {
                Initialize();
            }
        }
    }
}
