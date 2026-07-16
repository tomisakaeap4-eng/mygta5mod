using GTA;
using OpenAI;
using OpenAI.Chat;
using System;
using System.ClientModel;
using System.IO;

namespace FirstLegacyMod
{
    /// <summary>
    /// Lightweight AI communication service for chat bubble responses.
    /// Sends a prompt to the OpenAI-compatible API (NVIDIA NIM) and returns a
    /// Vietnamese response.
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
        // API key read from scripts/FirstGtaMod.ini → [NVIDIA] → NVIDIA_API_KEY.
        // Falls back to NVIDIA_API_KEY environment variable if .ini is missing.
        private const string BaseUrl = "https://integrate.api.nvidia.com/v1";
        private const string Model = "google/diffusiongemma-26b-a4b-it";
        private const string IniPath = "scripts\\FirstGtaMod.ini";

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

                string apiKey = ReadApiKeyFromIni();

                if (string.IsNullOrEmpty(apiKey))
                {
                    _initialized = true;  // Mark as initialized so we don't retry
                    return;               // _client stays null → IsAvailable = false
                }

                _client = new ChatClient(
                    model: Model,
                    credential: new ApiKeyCredential(apiKey),
                    options: new OpenAIClientOptions
                    {
                        Endpoint = new Uri(BaseUrl)
                    });

                _initialized = true;
            }
        }

        /// <summary>
        /// Reads NVIDIA_API_KEY from scripts/FirstGtaMod.ini [NVIDIA] section.
        /// Falls back to environment variable NVIDIA_API_KEY if .ini is missing.
        /// </summary>
        private static string ReadApiKeyFromIni()
        {
            try
            {
                if (File.Exists(IniPath))
                {
                    var settings = ScriptSettings.Load(IniPath);
                    string key = settings.GetValue<string>("NVIDIA", "NVIDIA_API_KEY", string.Empty);
                    if (!string.IsNullOrEmpty(key))
                        return key;
                }
            }
            catch
            {
                // .ini corrupted or unreadable — fall through to env var
            }

            return Environment.GetEnvironmentVariable("NVIDIA_API_KEY") ?? string.Empty;
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
                return "[AI] Chưa có NVIDIA_API_KEY.\nTạo file scripts\\FirstGtaMod.ini với:\n[NVIDIA]\nNVIDIA_API_KEY=your-key";
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
