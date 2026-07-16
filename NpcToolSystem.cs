using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using OpenAI.Chat;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace FirstLegacyMod
{
    /// <summary>
    /// Tool system for NPC AI function calling — bám sát OpenAI Example03 pattern.
    ///
    /// Defines tool schemas the AI can call, executes tool calls with in-game
    /// side-effects (animations, wanted stars, entity reactions), and provides
    /// a thread-safe queue so side-effects can be processed safely on the game
    /// thread during <c>Tick</c>.
    ///
    /// <b>Thread safety:</b> <see cref="ExecuteToolCall"/> is called from a
    /// background thread and only enqueues work via lambdas that capture Ped
    /// objects. <see cref="ProcessEffects"/> is called from the game thread
    /// and executes the queued actions.
    /// </summary>
    public static class NpcToolSystem
    {
        // ── Thread-safe queue for game-thread side-effects ──────────
        private static readonly ConcurrentQueue<Action> _pendingEffects
            = new ConcurrentQueue<Action>();

        // ════════════════════════════════════════════════════════════
        //  Tool Definitions (schemas gửi cho AI)
        // ════════════════════════════════════════════════════════════

        /// <summary>Gọi 911 — cảnh sát sẽ đến vị trí NPC.</summary>
        public static readonly ChatTool CallPoliceTool = ChatTool.CreateFunctionTool(
            functionName: "call_police",
            functionDescription:
                "Gọi điện thoại báo cảnh sát khẩn cấp đến vị trí hiện tại. " +
                "Cảnh sát sẽ đến bảo vệ bạn khỏi tên cướp đang dí súng."
            // functionParameters: null — no structured args needed
        );

        /// <summary>Quỳ xuống van xin tha mạng.</summary>
        public static readonly ChatTool BegOnKneesTool = ChatTool.CreateFunctionTool(
            functionName: "beg_on_knees",
            functionDescription:
                "Quỳ gối xuống đất, chắp tay van xin tha mạng một cách thảm thiết. " +
                "Thể hiện sự khuất phục và tuyệt vọng tột cùng."
        );

        /// <summary>Bỏ chạy thoát thân.</summary>
        public static readonly ChatTool AttemptEscapeTool = ChatTool.CreateFunctionTool(
            functionName: "attempt_escape",
            functionDescription:
                "Bất ngờ bỏ chạy thật nhanh khỏi tên cướp để thoát thân trong gang tấc."
        );

        /// <summary>La hét kêu cứu.</summary>
        public static readonly ChatTool ScreamForHelpTool = ChatTool.CreateFunctionTool(
            functionName: "scream_for_help",
            functionDescription:
                "La hét thất thanh kêu cứu, thu hút sự chú ý của người đi đường " +
                "xung quanh để được giúp đỡ."
        );

        /// <summary>Tất cả tool khả dụng.</summary>
        public static readonly IList<ChatTool> AllTools = new List<ChatTool>
        {
            CallPoliceTool,
            BegOnKneesTool,
            AttemptEscapeTool,
            ScreamForHelpTool,
        };

        // ════════════════════════════════════════════════════════════
        //  Public API
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Execute all queued game-thread side-effects.  Call from
        /// <c>Script.Tick</c> (game thread).
        /// </summary>
        public static void ProcessEffects()
        {
            while (_pendingEffects.TryDequeue(out Action effect))
            {
                try
                {
                    effect();
                }
                catch (Exception ex)
                {
                    Notification.Show($"~r~[TOOL ERR]~w~ {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Discard all queued side-effects.  Call when aborting or resetting
        /// to prevent stale effects from leaking into the next interaction.
        /// </summary>
        public static void ClearPendingEffects()
        {
            while (_pendingEffects.TryDequeue(out _)) { }
        }

        /// <summary>
        /// Execute a single tool call and return its result text.
        /// <b>Thread-safe:</b> can be called from a background thread;
        /// actual in-game side-effects are queued for the game thread
        /// via lambdas that capture <paramref name="npc"/> and
        /// <paramref name="player"/> by reference.
        /// </summary>
        /// <param name="toolCall">The tool call the AI requested.</param>
        /// <param name="npc">The NPC ped that called the tool.</param>
        /// <param name="player">The player ped.</param>
        /// <returns>A short result string to feed back into the AI conversation.</returns>
        public static string ExecuteToolCall(
            ChatToolCall toolCall, Ped npc, Ped player)
        {
            switch (toolCall.FunctionName)
            {
                case "call_police":
                    QueueCallPolice(npc, player);
                    return "Đã gọi 911 thành công. Cảnh sát đang trên đường đến vị trí của bạn.";

                case "beg_on_knees":
                    QueueBegOnKnees(npc, player);
                    return "Bạn đã quỳ gối xuống đất và đang van xin tha mạng.";

                case "attempt_escape":
                    QueueAttemptEscape(npc, player);
                    return "Bạn đã bỏ chạy khỏi tên cướp!";

                case "scream_for_help":
                    QueueScreamForHelp(npc);
                    return "Bạn đã la hét kêu cứu. Mọi người xung quanh đang chú ý.";

                default:
                    return $"Không thể thực hiện hành động '{toolCall.FunctionName}' lúc này.";
            }
        }

        // ════════════════════════════════════════════════════════════
        //  Tool effect implementations (queued for game thread)
        // ════════════════════════════════════════════════════════════

        private static void QueueCallPolice(Ped npc, Ped player)
        {
            _pendingEffects.Enqueue(() =>
            {
                if (npc == null || !npc.Exists()) return;

                // Stop surrender, play phone-call animation
                Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, npc.Handle);
                npc.BlockPermanentEvents = true;

                // WORLD_HUMAN_STAND_MOBILE scenario: phone call standing
                Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE,
                    npc.Handle, "WORLD_HUMAN_STAND_MOBILE", 0, true);

                // Raise player wanted level → cops will arrive
                if (player != null && player.Exists())
                {
                    int currentStars = Game.Player.WantedLevel;
                    Game.Player.WantedLevel = Math.Max(currentStars, 2);

                    Notification.Show(
                        "~r~🚔 911 ĐÃ ĐƯỢC GỌI!~w~ " +
                        "Cảnh sát đang trên đường đến. Coi chừng!");
                }
            });
        }

        private static void QueueBegOnKnees(Ped npc, Ped player)
        {
            _pendingEffects.Enqueue(() =>
            {
                if (npc == null || !npc.Exists()) return;

                // Stop current animation, play kneel
                Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, npc.Handle);
                npc.BlockPermanentEvents = true;

                // Load & play kneel animation (GTA V arrest anims)
                string kneelDict = "random@arrests";
                string kneelAnim = "kneeling_arrest_idle";
                if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, kneelDict))
                    Function.Call<bool>(Hash.REQUEST_ANIM_DICT, kneelDict);

                Function.Call(Hash.TASK_PLAY_ANIM,
                    npc.Handle,
                    kneelDict, kneelAnim,
                    8.0f,   // blend in speed
                    -1,     // duration (-1 = loop)
                    1,      // playback flag: 1 = loop
                    0f,     // blend out
                    false, false, false);

                // Still look at player
                if (player != null && player.Exists())
                {
                    Function.Call(Hash.TASK_LOOK_AT_ENTITY,
                        npc.Handle, player.Handle, -1, 2048, 2);
                }

                Notification.Show("~y~🙏 NPC quỳ gối van xin!");
            });
        }

        private static void QueueAttemptEscape(Ped npc, Ped player)
        {
            _pendingEffects.Enqueue(() =>
            {
                if (npc == null || !npc.Exists()) return;

                // Unblock events, clear tasks, flee
                npc.BlockPermanentEvents = false;
                Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, npc.Handle);

                if (player != null && player.Exists())
                {
                    // TASK_SMART_FLEE_PED: run away from player
                    Function.Call(Hash.TASK_SMART_FLEE_PED,
                        npc.Handle, player.Handle,
                        200f,    // flee distance
                        -1,      // time
                        false,   // prefer pavements
                        false);  // update to nearest pavement
                }
                else
                {
                    // Fallback: flee from current position
                    Vector3 pos = npc.Position;
                    Function.Call(Hash.TASK_SMART_FLEE_COORD,
                        npc.Handle,
                        pos.X, pos.Y, pos.Z,
                        200f, -1, false, false);
                }

                Notification.Show("~y~🏃 NPC bỏ chạy!");
            });
        }

        private static void QueueScreamForHelp(Ped npc)
        {
            _pendingEffects.Enqueue(() =>
            {
                if (npc == null || !npc.Exists()) return;

                // Stop current animation, play panic
                Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, npc.Handle);
                npc.BlockPermanentEvents = true;

                // Use a verified vanilla animation
                string panicDict = "random@mugging4";
                string panicAnim = "agitated_idle_a";
                if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, panicDict))
                    Function.Call<bool>(Hash.REQUEST_ANIM_DICT, panicDict);

                Function.Call(Hash.TASK_PLAY_ANIM,
                    npc.Handle,
                    panicDict, panicAnim,
                    8.0f, -1, 1, 0f, false, false, false);

                // Make nearby peds look at the screaming NPC
                Vector3 pos = npc.Position;
                Ped[] nearby = World.GetNearbyPeds(pos, 20f);
                foreach (Ped nearbyPed in nearby)
                {
                    if (nearbyPed.Handle == npc.Handle) continue;
                    if (nearbyPed.IsDead) continue;
                    if (!nearbyPed.IsHuman) continue;

                    // React briefly
                    Function.Call(Hash.TASK_LOOK_AT_ENTITY,
                        nearbyPed.Handle, npc.Handle, 4000, 0, 2);
                }

                Notification.Show("~o~📢 NPC la hét cầu cứu!");
            });
        }
    }

    /// <summary>
    /// Holds the result of a tool-enabled AI call — final text response plus
    /// the list of tool names that were executed.
    /// </summary>
    public sealed class AiToolResult
    {
        /// <summary>The final text response from the AI (Vietnamese).</summary>
        public string Response;

        /// <summary>Tool names executed (e.g. "call_police"), in order.</summary>
        public List<string> ExecutedTools = new List<string>();
    }
}
