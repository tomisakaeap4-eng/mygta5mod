using GTA;
using GTA.Math;
using GTA.Native;
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
    /// Designed to be extensible: new event types can be added by creating
    /// additional detection methods and response handlers in this class.
    /// </summary>
    public sealed class NpcEventSystem
    {
        // ── Per-NPC state machine ───────────────────────────────────

        private enum NpcState
        {
            /// <summary>NPC is surrendering, waiting for AI response.</summary>
            WaitingAi,
            /// <summary>AI has responded; bubble is showing the reply.</summary>
            ShowingResponse,
            /// <summary>Response bubble faded; counting down to release.</summary>
            Releasing,
        }

        private sealed class NpcEntry
        {
            public Ped NpcPed;              // Direct Ped reference from TargetedEntity
            public NpcState State;
            public ChatBubbleController Bubble = new ChatBubbleController();
            public int StateEnteredTick;    // GameTime when we entered current state
            public Task<string> PendingAiTask;
        }

        private readonly Dictionary<int, NpcEntry> _entries
            = new Dictionary<int, NpcEntry>();

        // ── Timing constants ────────────────────────────────────────
        private const int GlobalCooldownMs  = 2000;   // Min time between NPC activations
        private const int ReleaseDelayMs    = 2500;   // Extra wait after bubble fades
        private const int FreeAimScanFrames = 10;     // Only run free-aim fallback every N frames

        private int _lastActivationTick = int.MinValue;
        private int _frameCounter;

        // ════════════════════════════════════════════════════════════
        //  Public API
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Call once per frame from <c>Script.Tick</c>.
        /// Checks completed AI tasks, detects new events, updates
        /// state machines, and renders active bubbles.
        /// </summary>
        public void Update()
        {
            int now = Game.GameTime;

            Ped playerPed = Game.Player.Character;
            if (playerPed == null || !playerPed.Exists()) return;

            // ── 1. Process completed AI tasks ──────────────────────
            foreach (var kvp in _entries)
            {
                NpcEntry entry = kvp.Value;
                if (entry.State == NpcState.WaitingAi &&
                    entry.PendingAiTask != null &&
                    entry.PendingAiTask.IsCompleted)
                {
                    string response;
                    try { response = entry.PendingAiTask.Result; }
                    catch (AggregateException ex)
                    { response = $"[AI] {ex.InnerException?.Message ?? ex.Message}"; }

                    entry.Bubble.SetFixedMessage(response);
                    entry.State = NpcState.ShowingResponse;
                    entry.StateEnteredTick = now;
                    entry.PendingAiTask = null;
                }
            }

            // ── 2. Detect gunpoint events ──────────────────────────
            DetectGunpoint(playerPed, now);

            // ── 3. Update bubbles & state transitions ──────────────
            var removals = new List<int>();

            foreach (var kvp in _entries)
            {
                int handle = kvp.Key;
                NpcEntry entry = kvp.Value;
                Ped npc = entry.NpcPed;

                // NPC died or despawned → cleanup immediately
                if (npc == null || !npc.Exists() || npc.IsDead)
                {
                    entry.Bubble.Reset();
                    removals.Add(handle);
                    continue;
                }

                switch (entry.State)
                {
                    case NpcState.WaitingAi:
                    case NpcState.ShowingResponse:
                    {
                        // Keep rendering bubble above NPC
                        Vector3 headPos = npc.Position
                            + new Vector3(0f, 0f, entry.Bubble.HeightOffset);
                        entry.Bubble.Update(headPos);

                        // If bubble auto-hid (timeout), move to releasing
                        if (!entry.Bubble.IsActive)
                        {
                            entry.State = NpcState.Releasing;
                            entry.StateEnteredTick = now;
                        }
                        break;
                    }

                    case NpcState.Releasing:
                    {
                        // Keep NPC in surrender for a bit longer,
                        // then release and remove
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

        /// <summary>
        /// Release all tracked NPCs and reset state.
        /// Call from <c>Script.Aborted</c>.
        /// </summary>
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
        //  Event Detectors
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Detect player aiming a weapon at a human NPC.
        /// Triggers surrender + AI response for the targeted NPC.
        ///
        /// Two-stage detection:
        /// 1. <see cref="Player.TargetedEntity"/> — lock-on target (auto-aim)
        /// 2. <c>GET_ENTITY_PLAYER_IS_FREE_AIMING_AT</c> — free-aim crosshair
        ///    Fallback when the player is aiming without a lock-on.
        /// </summary>
        private void DetectGunpoint(Ped playerPed, int now)
        {
            if (!Game.Player.IsAiming) return;

            // Stage 1: Lock-on target (auto-aim)
            Entity target = Game.Player.TargetedEntity;

            // Stage 2: Free-aim crosshair fallback (throttled to every N frames)
            if (target == null && ++_frameCounter % FreeAimScanFrames == 0)
            {
                target = GetFreeAimTarget(Game.Player, playerPed.Position);
            }

            if (target == null || !(target is Ped)) return;

            Ped npc = (Ped)target;
            if (npc.Handle == playerPed.Handle) return;
            if (!npc.Exists() || npc.IsDead) return;
            if (!npc.IsHuman) return;
            if (npc.IsInVehicle()) return;

            int handle = npc.Handle;
            if (_entries.ContainsKey(handle)) return;

            // Global cooldown to avoid spam when player sweeps aim
            if (now - _lastActivationTick < GlobalCooldownMs) return;

            _lastActivationTick = now;

            // ── Create entry & trigger surrender ───────────────────
            var entry = new NpcEntry
            {
                NpcPed = npc,
                State = NpcState.WaitingAi,
                StateEnteredTick = now,
                PendingAiTask = Task.Run(() =>
                    AIChatService.GetSituationalResponse(
                        GunpointSystemPrompt,
                        GunpointUserPrompt)),
            };

            SurrenderNpc(npc, playerPed);
            entry.Bubble.StartWaiting();

            _entries[handle] = entry;
        }

        /// <summary>
        /// Uses <c>GET_ENTITY_PLAYER_IS_FREE_AIMING_AT</c> to find what
        /// entity is under the player's crosshair when free-aiming
        /// (no lock-on).  Searches nearby peds to wrap the raw handle
        /// into a <see cref="Ped"/> reference.
        /// </summary>
        /// <returns>The targeted <see cref="Ped"/>, or <c>null</c>.</returns>
        private static Ped GetFreeAimTarget(Player player, Vector3 playerPos)
        {
            // GET_ENTITY_PLAYER_IS_FREE_AIMING_AT(player, entity*)
            // Hash not in SHVDN enum – use raw value
            using (var outEntity = new OutputArgument())
            {
                bool found = Function.Call<bool>(
                    (Hash)0x2975C866E6713290,
                    player.Handle, outEntity);

                if (!found) return null;

                int handle = outEntity.GetResult<int>();
                if (handle == 0) return null;

                // Resolve handle → Ped wrapper via nearby ped search
                // Radius 120 m covers everything in aiming range
                Ped[] nearby = World.GetNearbyPeds(playerPos, 120f);
                for (int i = 0; i < nearby.Length; i++)
                {
                    if (nearby[i].Handle == handle)
                        return nearby[i];
                }

                return null;
            }
        }

        // ════════════════════════════════════════════════════════════
        //  NPC Behavior Helpers
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Force the NPC into a hands-up surrender pose facing the player,
        /// and block the NPC's default flee/combat AI.
        /// </summary>
        private static void SurrenderNpc(Ped npc, Ped playerPed)
        {
            npc.BlockPermanentEvents = true;

            // TASK_HANDS_UP(ped, duration, facingPed, timeToFacePed, flags)
            // duration -1 = hold forever, timeToFacePed -1 = instant
            Function.Call(Hash.TASK_HANDS_UP,
                npc.Handle, -1, playerPed.Handle, -1, 0);

            // TASK_LOOK_AT_ENTITY(ped, lookAt, duration, flags, priority)
            Function.Call(Hash.TASK_LOOK_AT_ENTITY,
                npc.Handle, playerPed.Handle, -1, 2048, 2);
        }

        /// <summary>
        /// Restore the NPC to normal game AI.
        /// </summary>
        private static void ReleaseNpc(Ped npc)
        {
            npc.BlockPermanentEvents = false;
            Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, npc.Handle);
        }

        // ════════════════════════════════════════════════════════════
        //  AI Prompts — Gunpoint event
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
