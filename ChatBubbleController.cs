using GTA;
using GTA.Math;
using GTA.Native;
using System;

namespace FirstLegacyMod
{
    /// <summary>
    /// Per‑entity bubble controller.
    /// Manages active state, world‑to‑screen projection, auto‑hide timeout,
    /// and delegates drawing to <see cref="ChatBubble"/>.
    ///
    /// Refactored for AI responses: message rotation removed;
    /// <see cref="SetFixedMessage"/> is the primary API for AI responses.
    /// Supports a "waiting" mode that shows a placeholder with a live
    /// elapsed‑time counter via <see cref="StartWaiting"/>.
    /// </summary>
    public sealed class ChatBubbleController
    {
        // ── Instance state ─────────────────────────────────────────────
        private int _autoHideEndTick;   // GameTime when auto-hide should fire
        private bool _autoHideEnabled;  // Whether auto-hide is active

        // ── Waiting mode ───────────────────────────────────────────────
        private bool _isWaiting;        // True while waiting for AI response
        private int _waitStartTick;     // GameTime when waiting started

        /// <summary>Whether the bubble is currently visible.</summary>
        public bool IsActive { get; set; }

        /// <summary>The text currently displayed in the bubble.</summary>
        public string CurrentMessage { get; set; } = string.Empty;

        /// <summary>
        /// How high above the entity position to place the bubble (metres).
        /// </summary>
        public float HeightOffset { get; set; } = 1.2f;

        // ── Public API ─────────────────────────────────────────────────

        /// <summary>
        /// Set a message and activate the bubble.
        /// The bubble auto-hides after a duration proportional to text length
        /// (min 3s, +0.3s per word, max 60s for long AI responses).
        /// Also exits waiting mode if active.
        /// </summary>
        /// <param name="message">The text to display in the bubble.</param>
        public void SetFixedMessage(string message)
        {
            _isWaiting = false;
            CurrentMessage = message;
            IsActive = true;

            // Duration: min 3s, +0.3s per word, max 60s
            int wordCount = message.Split(
                new[] { ' ', '\n', '\r' },
                StringSplitOptions.RemoveEmptyEntries).Length;

            int durationMs = Math.Min(60000, 3000 + Math.Max(0, wordCount - 1) * 300);

            _autoHideEndTick = Game.GameTime + durationMs;
            _autoHideEnabled = true;
        }

        /// <summary>
        /// Enter waiting mode: show a placeholder bubble with a live
        /// elapsed‑time counter that updates every frame.
        /// Auto‑hide is disabled while waiting.
        /// </summary>
        public void StartWaiting()
        {
            _isWaiting = true;
            _waitStartTick = Game.GameTime;
            _autoHideEnabled = false;
            IsActive = true;
            CurrentMessage = BuildWaitingMessage(0);
        }

        /// <summary>
        /// Call once per frame from <c>Script.Tick</c>.
        /// Projects the world position to screen and draws the bubble.
        /// In waiting mode, updates the elapsed‑time counter each frame.
        /// </summary>
        /// <param name="worldPosition">
        /// World position above the entity (e.g. <c>ped.Position + (0,0,1.2)</c>).
        /// </param>
        public void Update(Vector3 worldPosition)
        {
            if (!IsActive)
            {
                return;
            }

            // ── Waiting mode: update elapsed time counter each frame ──
            if (_isWaiting)
            {
                int elapsedMs = Game.GameTime - _waitStartTick;
                CurrentMessage = BuildWaitingMessage(elapsedMs);
            }

            // Auto-hide on timeout (disabled while waiting)
            if (_autoHideEnabled && Game.GameTime >= _autoHideEndTick)
            {
                Reset();
                return;
            }

            // World → screen projection
            using (var outX = new OutputArgument())
            using (var outY = new OutputArgument())
            {
                Function.Call<int>(
                    (Hash)0xF9904D11F1ACBEC3, // GET_SCREEN_COORD_FROM_WORLD_COORD
                    worldPosition.X, worldPosition.Y, worldPosition.Z,
                    outX, outY);

                float sx = outX.GetResult<float>();
                float sy = outY.GetResult<float>();

                ChatBubble.Draw(sx, sy, CurrentMessage);
            }
        }

        /// <summary>
        /// Reset the bubble state (call from <c>Script.Aborted</c> or Esc).
        /// </summary>
        public void Reset()
        {
            IsActive = false;
            CurrentMessage = string.Empty;
            _autoHideEnabled = false;
            _isWaiting = false;
        }

        // ── Waiting-mode helper ────────────────────────────────────

        /// <summary>
        /// Build the waiting placeholder message with a live elapsed-time counter.
        /// </summary>
        /// <param name="elapsedMs">Elapsed milliseconds since waiting started.</param>
        private static string BuildWaitingMessage(int elapsedMs)
        {
            float seconds = elapsedMs / 1000f;
            return $"Đang đợi AI phản hồi...\nĐã đợi: {seconds:F1}s";
        }
    }
}
