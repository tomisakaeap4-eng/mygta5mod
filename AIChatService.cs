using GTA;
using OpenAI;
using OpenAI.Chat;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.IO;
using System.Net;

namespace FirstLegacyMod
{
    /// <summary>
    /// Lightweight AI communication service for chat bubble responses.
    /// Uses OpenAI-compatible API (local DeepSeek by default, overridable via .ini).
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

        // ── Default configuration (local DeepSeek) ──────────────────────
        private const string DefaultApiKey = "sk-95a1ecf324105598-e8n9c5-8221cc03";
        private const string DefaultBaseUrl = "http://localhost:20128/v1";
        private const string DefaultModel = "oc/deepseek-v4-flash-free";

        private const string IniPath = "scripts\\FirstGtaMod.ini";
        private const string LogPath = "scripts\\FirstGtaMod.log";

        // ── Instance state ────────────────────────────────────────────
        private static readonly object _lock = new object();
        private static ChatClient _client;
        private static bool _initialized;
        private static string _lastResponse = string.Empty;
        private static string _logPath;
        private static bool _logStarted;
        private static readonly object _logLock = new object();

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
        /// Initialize the ChatClient.  Uses hardcoded local defaults;
        /// overridable via scripts/FirstGtaMod.ini [AI] → API_KEY.
        /// Safe to call multiple times.
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;

            lock (_lock)
            {
                if (_initialized) return;

                _logPath = LogPath;
                StartLog("=== FirstGtaMod AI Log — session start ===");

                // Resolve effective config (ini overrides only if user provided a real key)
                string apiKey = ResolveApiKey();
                string baseUrl = ResolveBaseUrl();
                string model = ResolveModel();

                if (string.IsNullOrEmpty(apiKey))
                {
                    WriteLog("[AIChatService] No API key available. AI disabled.");
                    _initialized = true;
                    return;
                }

                string masked = apiKey.Length > 8
                    ? "..." + apiKey.Substring(apiKey.Length - 4)
                    : "***";
                WriteLog($"[AIChatService] Using API key ending in {masked}, endpoint={baseUrl}, model={model}");

                // Enable TLS 1.2 for HTTPS endpoints (NVIDIA etc.) — harmless for HTTP localhost
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                _client = new ChatClient(
                    model: model,
                    credential: new ApiKeyCredential(apiKey),
                    options: new OpenAIClientOptions
                    {
                        Endpoint = new Uri(baseUrl)
                    });

                _initialized = true;
            }
        }

        /// <summary>
        /// Sends a request to the AI and returns a random Vietnamese response.
        /// This method is synchronous and will block the calling thread.
        /// Call from a background thread via <c>Task.Run</c>.
        /// </summary>
        public static string GetVietnameseResponse()
        {
            return GetSituationalResponse(SystemPrompt, UserPrompt);
        }

        /// <summary>
        /// Sends a request to the AI with a custom situation-specific prompt.
        /// This method is synchronous and will block the calling thread.
        /// Call from a background thread via <c>Task.Run</c>.
        /// </summary>
        public static string GetSituationalResponse(
            string systemPrompt, string userPrompt)
        {
            EnsureInitialized();

            if (_client == null)
            {
                return "[AI] Chưa có API key.\n" +
                       "Tạo file scripts\\FirstGtaMod.ini với:\n" +
                       "[AI]\nAPI_KEY=your-key-here";
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
                    Temperature = 1.2f,
                };

                ChatCompletion completion = _client.CompleteChat(messages, options);

                string response = completion.Content[0]?.Text?.Trim() ?? string.Empty;

                // Avoid repeating the same response
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
                string detail = FlattenException(ex);
                WriteLog($"[AIChatService] API call failed: {detail}");
                return $"[AI Lỗi] {ex.Message}\n(Check scripts/FirstGtaMod.log)";
            }
        }

        /// <summary>
        /// Sends a request to the AI with function-calling tools enabled.
        /// Follows the OpenAI Example03 pattern: loop until the AI stops
        /// requesting tool calls, executing each tool via
        /// <see cref="NpcToolSystem.ExecuteToolCall"/>.
        ///
        /// This method is synchronous and will block the calling thread.
        /// Call from a background thread via <c>Task.Run</c>.
        /// </summary>
        /// <param name="systemPrompt">System message setting NPC persona.</param>
        /// <param name="userPrompt">User message describing the situation.</param>
        /// <param name="tools">Tool definitions the AI may call.</param>
        /// <param name="npc">The NPC ped (for tool context, captured in closure).</param>
        /// <param name="player">The player ped.</param>
        /// <returns>
        /// An <see cref="AiToolResult"/> with the final Vietnamese text and
        /// the list of tool names that were executed.
        /// </returns>
        public static AiToolResult GetResponseWithTools(
            string systemPrompt,
            string userPrompt,
            IList<ChatTool> tools,
            Ped npc,
            Ped player)
        {
            EnsureInitialized();

            if (_client == null)
            {
                return new AiToolResult
                {
                    Response = "[AI] Chưa có API key.\n" +
                               "Tạo file scripts\\FirstGtaMod.ini với:\n" +
                               "[AI]\nAPI_KEY=your-key-here",
                };
            }

            var executedTools = new List<string>();
            const int maxIterations = 5;

            try
            {
                var messages = new List<ChatMessage>
                {
                    new SystemChatMessage(systemPrompt),
                    new UserChatMessage(userPrompt),
                };

                ChatCompletionOptions options = new ChatCompletionOptions
                {
                    MaxOutputTokenCount = 150,
                    Temperature = 1.2f,
                };

                // Register all tools
                foreach (ChatTool tool in tools)
                {
                    options.Tools.Add(tool);
                }

                // ── Tool-calling loop (pattern: Example03_FunctionCalling.cs) ──
                bool requiresAction;
                int iteration = 0;
                do
                {
                    if (++iteration > maxIterations)
                    {
                        WriteLog("[AIChatService] Tool-call loop exceeded max iterations — breaking.");
                        break;
                    }

                    requiresAction = false;
                    ChatCompletion completion = _client.CompleteChat(messages, options);

                    switch (completion.FinishReason)
                    {
                        case ChatFinishReason.Stop:
                            // Assistant gave final text — add to history
                            messages.Add(new AssistantChatMessage(completion));
                            break;

                        case ChatFinishReason.ToolCalls:
                            // Assistant wants to call tools
                            messages.Add(new AssistantChatMessage(completion));

                            foreach (ChatToolCall toolCall in completion.ToolCalls)
                            {
                                string toolResult = NpcToolSystem.ExecuteToolCall(
                                    toolCall, npc, player);
                                executedTools.Add(toolCall.FunctionName);
                                messages.Add(new ToolChatMessage(toolCall.Id, toolResult));

                                WriteLog($"[AIChatService] Tool called: {toolCall.FunctionName}");
                            }

                            requiresAction = true;
                            break;

                        case ChatFinishReason.Length:
                            return new AiToolResult
                            {
                                Response = "[AI] Phản hồi bị cắt ngắn. Thử lại sau.",
                                ExecutedTools = executedTools,
                            };

                        case ChatFinishReason.ContentFilter:
                            return new AiToolResult
                            {
                                Response = "[AI] Nội dung bị lọc. Thử lại sau.",
                                ExecutedTools = executedTools,
                            };

                        default:
                            return new AiToolResult
                            {
                                Response = $"[AI] Lỗi: {completion.FinishReason}",
                                ExecutedTools = executedTools,
                            };
                    }
                } while (requiresAction);

                // ── Extract final assistant message text ─────────
                string finalResponse = string.Empty;
                for (int i = messages.Count - 1; i >= 0; i--)
                {
                    if (messages[i] is AssistantChatMessage assistantMsg
                        && assistantMsg.Content.Count > 0)
                    {
                        finalResponse = assistantMsg.Content[0].Text?.Trim() ?? string.Empty;
                        break;
                    }
                }

                _lastResponse = finalResponse;

                if (string.IsNullOrEmpty(finalResponse))
                {
                    finalResponse = "[AI] Không nhận được phản hồi. Thử lại sau.";
                }

                return new AiToolResult
                {
                    Response = finalResponse,
                    ExecutedTools = executedTools,
                };
            }
            catch (Exception ex)
            {
                string detail = FlattenException(ex);
                WriteLog($"[AIChatService] Tool-calling API failed: {detail}");
                return new AiToolResult
                {
                    Response = $"[AI Lỗi] {ex.Message}\n(Check scripts/FirstGtaMod.log)",
                    ExecutedTools = executedTools,
                };
            }
        }

        // ── Config resolution ─────────────────────────────────────────

        /// <summary>
        /// Reads API_KEY from .ini [AI] section.
        /// If missing, empty, or looks like a placeholder, returns hardcoded default.
        /// </summary>
        private static string ResolveApiKey()
        {
            string iniKey = ReadIniValue("AI", "API_KEY");

            // Reject obvious placeholders / template values
            if (string.IsNullOrEmpty(iniKey) ||
                iniKey.StartsWith("your-", StringComparison.OrdinalIgnoreCase) ||
                iniKey.Length < 16)
            {
                WriteLog("[AIChatService] .ini key is empty/template — using hardcoded default.");
                return DefaultApiKey;
            }

            WriteLog("[AIChatService] Using API key from .ini override.");
            return iniKey;
        }

        private static string ResolveBaseUrl()
        {
            string iniVal = ReadIniValue("AI", "BASE_URL");
            if (!string.IsNullOrEmpty(iniVal) && Uri.TryCreate(iniVal, UriKind.Absolute, out _))
            {
                WriteLog($"[AIChatService] Using BASE_URL from .ini: {iniVal}");
                return iniVal;
            }
            return DefaultBaseUrl;
        }

        private static string ResolveModel()
        {
            string iniVal = ReadIniValue("AI", "MODEL");
            if (!string.IsNullOrEmpty(iniVal))
            {
                WriteLog($"[AIChatService] Using MODEL from .ini: {iniVal}");
                return iniVal;
            }
            return DefaultModel;
        }

        /// <summary>
        /// Reads a value from scripts/FirstGtaMod.ini.  Returns empty string if
        /// the file is missing, section/key not found, or file is corrupt.
        /// </summary>
        private static string ReadIniValue(string section, string key)
        {
            try
            {
                if (File.Exists(IniPath))
                {
                    var settings = ScriptSettings.Load(IniPath);
                    return settings.GetValue<string>(section, key, string.Empty) ?? string.Empty;
                }
            }
            catch
            {
                // .ini corrupted or unreadable — ignore
            }

            return string.Empty;
        }

        // ── Internal ──────────────────────────────────────────────────

        private static void EnsureInitialized()
        {
            if (!_initialized)
            {
                Initialize();
            }
        }

        private static string FlattenException(Exception ex)
        {
            var parts = new List<string>();
            while (ex != null)
            {
                parts.Add($"[{ex.GetType().Name}] {ex.Message}");
                ex = ex.InnerException;
            }
            return string.Join(" <- ", parts);
        }

        private static void StartLog(string message)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                lock (_logLock)
                {
                    File.WriteAllText(_logPath,
                        $"[{timestamp}] {message}{Environment.NewLine}");
                    _logStarted = true;
                }
            }
            catch { }
        }

        private static void WriteLog(string message)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string line = $"[{timestamp}] {message}{Environment.NewLine}";
                lock (_logLock)
                {
                    if (!_logStarted)
                    {
                        string header = $"[{timestamp}] === FirstGtaMod AI Log (late init) — {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}";
                        File.WriteAllText(_logPath, header);
                        _logStarted = true;
                    }

                    File.AppendAllText(_logPath, line);
                }
            }
            catch { }
        }
    }
}
