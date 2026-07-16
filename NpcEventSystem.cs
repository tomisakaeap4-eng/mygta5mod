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
            public Task<string> PendingAiTask;
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
                string response;
                try { response = entry.PendingAiTask.Result; }
                catch (AggregateException ex)
                { response = $"[AI] {ex.InnerException?.Message ?? ex.Message}"; }

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
                    entry.Bubble.SetFixedMessage(response);
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

            // Only one NPC at a time
            if (HasActiveEntry())
            {
                if (shouldLog)
                    Notification.Show($"~y~[NPC DEBUG]~w~ already busy with another NPC -> skip");
                return;
            }

            if (now - _lastActivationTick < GlobalCooldownMs)
            {
                if (shouldLog)
                    Notification.Show($"~y~[NPC DEBUG]~w~ cooldown {now - _lastActivationTick}ms -> skip");
                return;
            }

            _lastActivationTick = now;
            int handle = bestNpc.Handle;

            Notification.Show($"~g~[NPC EVENT]~w~ NPC #{handle} surrendering...");

            var entry = new NpcEntry
            {
                NpcPed = bestNpc,
                State = NpcState.WaitingAi,
                StateEnteredTick = now,
                PendingAiTask = Task.Run(() =>
                    AIChatService.GetSituationalResponse(
                        GunpointSystemPrompt,
                        GunpointUserPrompt)),
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
            "Bạn rất hoảng sợ và đã giơ tay đầu hàng. " +
            "Hãy trả lời bằng tiếng Việt, NGẮN GỌN (1-2 câu, dưới 200 ký tự), " +
            "thể hiện sự sợ hãi tột độ, van xin tha mạng, hoặc hoảng loạn. " +
            "Phản ứng phải chân thực như một con người thật trước tình huống " +
            "sinh tử. Không lặp lại câu đã nói trước đó.";

        private const string GunpointUserPrompt =
            "Tên cướp đang dí súng vào mặt bạn. Bạn sẽ nói gì?";
    }
}
