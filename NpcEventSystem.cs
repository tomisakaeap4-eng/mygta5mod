using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FirstLegacyMod
{
    /// <summary>
    /// Manages NPC interaction events: detects player actions (e.g. aiming
    /// a gun at an NPC), triggers NPC behaviors (e.g. hands-up surrender),
    /// sends context-aware AI prompts, and displays AI responses in chat
    /// bubbles above affected NPCs.
    ///
    /// Optimisation: cancels pending AI requests when the player stops
    /// aiming or the NPC drifts out of range.
    /// </summary>
    public sealed class NpcEventSystem
    {
        // ── Per-NPC state machine ───────────────────────────────────

        private enum NpcState
        {
            WaitingAi,
            ShowingResponse,
            Releasing,
        }

        private sealed class NpcEntry
        {
            public Ped NpcPed;
            public NpcState State;
            public ChatBubbleController Bubble = new ChatBubbleController();
            public int StateEnteredTick;
            public Task<AiToolResult> PendingAiTask;
            public bool Cancelled;  // true = player stopped aiming / NPC wandered off
        }

        private readonly Dictionary<int, NpcEntry> _entries
            = new Dictionary<int, NpcEntry>();

        // ── Timing constants ────────────────────────────────────────
        private const int GlobalCooldownMs  = 500;
        private const int ReleaseDelayMs    = 2500;
        private const int CancelDistance    = 50;    // metres
        private const int DebugLogFrames    = 90;

        private int _lastActivationTick = -GlobalCooldownMs;
        private int _debugFrameCounter;

        /// <summary>
        /// Returns true if any NPC is currently active (waiting for AI
        /// or showing a response).  Used to enforce 1-at-a-time limit.
        /// </summary>
        private bool HasActiveEntry()
        {
            foreach (var kvp in _entries)
            {
                NpcState s = kvp.Value.State;
                if (s == NpcState.WaitingAi || s == NpcState.ShowingResponse)
                    return true;
            }
            return false;
        }

        // ── Ped type constants (from ePedType enum) ────────────────
        private const int PedTypeCop   = 6;
        private const int PedTypeSwat  = 27;
        private const int PedTypeArmy  = 29;

        /// <summary>
        /// Returns true if the ped is law enforcement (cop, SWAT, army).
        /// Uses native GET_PED_TYPE (0xFF059E1E4C01E63C).
        /// </summary>
        private static bool IsLawEnforcement(Ped npc)
        {
            int pedType = Function.Call<int>((Hash)0xFF059E1E4C01E63C, npc.Handle);
            return pedType == PedTypeCop
                || pedType == PedTypeSwat
                || pedType == PedTypeArmy;
        }

        // ════════════════════════════════════════════════════════════
        //  Public API
        // ════════════════════════════════════════════════════════════

        public void Update()
        {
            int now = Game.GameTime;

            Ped playerPed = Game.Player.Character;
            if (playerPed == null || !playerPed.Exists()) return;

            bool isAiming = Game.Player.IsAiming;

            if (isAiming)
            {
                Function.Call((Hash)0x9911F4A24485F653, true);
            }

            // ── 0. Process any queued tool side-effects on game thread ─
            NpcToolSystem.ProcessEffects();

            // ── 1. Process completed AI tasks ──────────────────────
            var completedEntries = new List<NpcEntry>();
            foreach (var kvp in _entries)
            {
                NpcEntry e = kvp.Value;
                if (e.State == NpcState.WaitingAi &&
                    e.PendingAiTask != null &&
                    e.PendingAiTask.IsCompleted &&
                    !e.Cancelled)  // skip already-cancelled to avoid redundant work
                {
                    completedEntries.Add(e);
                }
            }

            foreach (var entry in completedEntries)
            {
                AiToolResult result;
                try { result = entry.PendingAiTask.Result; }
                catch (AggregateException ex)
                { result = new AiToolResult { Response = $"[AI] {ex.InnerException?.Message ?? ex.Message}", ExecutedTools = new List<string>() }; }

                entry.PendingAiTask = null;

                if (entry.Cancelled)
                {
                    // Response arrived but we already released the NPC
                    entry.Bubble.Reset();
                    if (entry.State == NpcState.WaitingAi)
                    {
                        entry.State = NpcState.Releasing;
                        entry.StateEnteredTick = now;
                    }
                }
                else
                {
                    entry.Bubble.SetFixedMessage(result.Response);
                    entry.State = NpcState.ShowingResponse;
                    entry.StateEnteredTick = now;
                }
            }

            // ── 2. Detect gunpoint OR cancel on stop-aim ───────────
            if (isAiming)
            {
                DetectGunpoint(playerPed, now);
            }
            else
            {
                CancelWaitingEntries(playerPed, now);
            }

            // ── 3. Update bubbles & state transitions ──────────────
            var removals = new List<int>();

            foreach (var kvp in _entries)
            {
                int handle = kvp.Key;
                NpcEntry entry = kvp.Value;
                Ped npc = entry.NpcPed;

                if (npc == null || !npc.Exists() || npc.IsDead)
                {
                    entry.Bubble.Reset();
                    removals.Add(handle);
                    continue;
                }

                switch (entry.State)
                {
                    case NpcState.WaitingAi:
                    {
                        // Cancel if NPC drifted too far
                        float dist = (npc.Position - playerPed.Position).Length();
                        if (dist > CancelDistance)
                        {
                            CancelEntry(entry, npc, handle, removals,
                                $"NPC #{handle} too far ({dist:F0}m)");
                            break;
                        }

                        Vector3 headPos = npc.Position
                            + new Vector3(0f, 0f, entry.Bubble.HeightOffset);
                        entry.Bubble.Update(headPos);
                        break;
                    }

                    case NpcState.ShowingResponse:
                    {
                        Vector3 headPos = npc.Position
                            + new Vector3(0f, 0f, entry.Bubble.HeightOffset);
                        entry.Bubble.Update(headPos);

                        if (!entry.Bubble.IsActive)
                        {
                            entry.State = NpcState.Releasing;
                            entry.StateEnteredTick = now;
                        }
                        break;
                    }

                    case NpcState.Releasing:
                    {
                        if (now - entry.StateEnteredTick >= ReleaseDelayMs)
                        {
                            ReleaseNpc(npc);
                            removals.Add(handle);
                        }
                        break;
                    }
                }
            }

            foreach (int h in removals)
                _entries.Remove(h);
        }

        public void Reset()
        {
            foreach (var kvp in _entries)
            {
                NpcEntry entry = kvp.Value;
                Ped npc = entry.NpcPed;
                if (npc != null && npc.Exists())
                    ReleaseNpc(npc);
                entry.Bubble.Reset();
            }
            _entries.Clear();
            NpcToolSystem.ClearPendingEffects();
        }

        // ════════════════════════════════════════════════════════════
        //  Cancel / Abort helpers
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Called when the player stops aiming.  Cancels all entries
        /// that are still waiting for an AI response.
        /// </summary>
        private void CancelWaitingEntries(Ped playerPed, int now)
        {
            var removals = new List<int>();
            foreach (var kvp in _entries)
            {
                NpcEntry entry = kvp.Value;
                if (entry.State != NpcState.WaitingAi) continue;

                int handle = kvp.Key;
                CancelEntry(entry, entry.NpcPed, handle, removals,
                    $"NPC #{handle} — player stopped aiming");
            }

            foreach (int h in removals)
                _entries.Remove(h);

            // Discard any tool effects queued for cancelled NPCs
            NpcToolSystem.ClearPendingEffects();
        }

        /// <summary>
        /// Marks an entry as cancelled, releases the NPC, and adds to
        /// the removal list.
        /// </summary>
        private static void CancelEntry(NpcEntry entry, Ped npc,
            int handle, List<int> removals, string reason)
        {
            entry.Cancelled = true;
            if (npc != null && npc.Exists())
                ReleaseNpc(npc);
            entry.Bubble.Reset();
            removals.Add(handle);

            Notification.Show($"~r~[NPC CANCEL]~w~ {reason}");
        }

        // ════════════════════════════════════════════════════════════
        //  Detection — angle-based crosshair search
        // ════════════════════════════════════════════════════════════

        private void DetectGunpoint(Ped playerPed, int now)
        {
            // Short-circuit: don't scan if already processing another NPC
            if (HasActiveEntry() || now - _lastActivationTick < GlobalCooldownMs)
                return;

            bool shouldLog = ++_debugFrameCounter % DebugLogFrames == 0;

            Vector3 aimDir = GameplayCamera.Direction;
            Vector3 playerPos = playerPed.Position;
            Ped[] nearby = World.GetNearbyPeds(playerPos, 40f);

            Ped bestNpc = null;
            float bestDist = float.MaxValue;

            for (int i = 0; i < nearby.Length; i++)
            {
                Ped npc = nearby[i];
                if (npc.Handle == playerPed.Handle) continue;
                if (!npc.Exists() || npc.IsDead) continue;
                if (!npc.IsHuman) continue;
                if (IsLawEnforcement(npc)) continue;
                if (npc.IsInVehicle()) continue;
                if (_entries.ContainsKey(npc.Handle)) continue;

                Vector3 toNpc = npc.Position - playerPos;
                float dist = toNpc.Length();
                if (dist > 35f) continue;

                Vector3 dirToNpc = toNpc / dist;
                float dot = Vector3.Dot(aimDir, dirToNpc);
                if (dot < 0.965f) continue; // ~15° cone

                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestNpc = npc;
                }
            }

            if (shouldLog)
            {
                Notification.Show(bestNpc != null
                    ? $"~y~[NPC DEBUG]~w~ Found NPC h={bestNpc.Handle} dist={bestDist:F1}m"
                    : $"~y~[NPC DEBUG]~w~ nearby={nearby.Length} no NPC in crosshair cone");
            }

            if (bestNpc == null) return;

            _lastActivationTick = now;
            int handle = bestNpc.Handle;

            Notification.Show($"~g~[NPC EVENT]~w~ NPC #{handle} surrendering...");

            var entry = new NpcEntry
            {
                NpcPed = bestNpc,
                State = NpcState.WaitingAi,
                StateEnteredTick = now,
                PendingAiTask = Task.Run(() =>
                    AIChatService.GetResponseWithTools(
                        GunpointSystemPrompt,
                        GunpointUserPrompt,
                        NpcToolSystem.AllTools,
                        bestNpc,
                        playerPed)),
            };

            SurrenderNpc(bestNpc, playerPed);
            entry.Bubble.StartWaiting();
            _entries[handle] = entry;
        }

        // ════════════════════════════════════════════════════════════
        //  NPC Behavior Helpers
        // ════════════════════════════════════════════════════════════

        private static void SurrenderNpc(Ped npc, Ped playerPed)
        {
            Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, npc.Handle);
            npc.BlockPermanentEvents = true;
            Function.Call(Hash.TASK_HANDS_UP,
                npc.Handle, -1, playerPed.Handle, -1, 0);
            Function.Call(Hash.TASK_LOOK_AT_ENTITY,
                npc.Handle, playerPed.Handle, -1, 2048, 2);
        }

        private static void ReleaseNpc(Ped npc)
        {
            npc.BlockPermanentEvents = false;
            Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, npc.Handle);
        }

        // ════════════════════════════════════════════════════════════
        //  AI Prompts
        // ════════════════════════════════════════════════════════════

        private const string GunpointSystemPrompt =
            "Bạn là một người dân bình thường ở thành phố Los Santos. " +
            "Một tên cướp có vũ trang đang chĩa súng vào bạn. " +
            "Bạn rất hoảng sợ. Bạn có thể dùng CÁC HÀNH ĐỘNG SAU:\n" +
            "- call_police: Gọi 911 báo cảnh sát.\n" +
            "- beg_on_knees: Quỳ gối van xin tha mạng.\n" +
            "- attempt_escape: Bỏ chạy thoát thân.\n" +
            "- scream_for_help: La hét kêu cứu.\n\n" +
            "LUÔN gọi ÍT NHẤT MỘT HÀNH ĐỘNG. " +
            "Sau đó CHỈ trả về DUY NHẤT CÂU NÓI TRỰC TIẾP của bạn (tiếng Việt, " +
            "1-2 câu ngắn gọn, dưới 200 ký tự).\n" +
            "TUYỆT ĐỐI KHÔNG: mô tả hành động, không dùng dấu ngoặc kép, " +
            "không viết kiểu tường thuật (\"tôi đang...\"), không dẫn lời. " +
            "Chỉ xuất ra lời nói thuần túy. Ví dụ đúng: Ôi đừng bắn tôi!\n" +
            "Ví dụ sai: Tôi quỳ xuống và nói \"xin đừng bắn\".";

        private const string GunpointUserPrompt =
            "Tên cướp dí súng vào mặt bạn. " +
            "Gọi hành động + chỉ trả về lời nói trực tiếp.";
    }
}
