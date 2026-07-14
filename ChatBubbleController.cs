using GTA;
using GTA.Math;
using GTA.Native;
using System;

namespace FirstLegacyMod
{
    /// <summary>
    /// Per‑entity bubble controller.
    /// Manages active state, random message rotation, world‑to‑screen
    /// projection and delegates drawing to <see cref="ChatBubble"/>.
    /// </summary>
    public sealed class ChatBubbleController
    {
        // ── Random messages ────────────────────────────────────────────
        private static readonly string[] DefaultMessages =
        {
            "Hello!",
            "Nice weather today!",
            "Watch out!",
            "Follow me!",
            "Let's go!",
            "GG!",
            "YOLO!",
            "Don't shoot!",
            "Need backup!",
            "Good job!",
            "Where to next?",
            "Cover me!",
            "Stay low!",
            "Target spotted!",
            "I'm friendly!",
            "That was close!",
            "Run!",
            "Moving out!",
            "Mission complete!",
            "Noob detected!",
            "What's up?",
            "Lol!",
            "Be careful!",
            "On my way!",
            "Let's party!",
            "I need help!",
            "Nice shot!",
            "Gotcha!",
            "See ya!",
            "Peace out!"
        };

        // ── Instance state ─────────────────────────────────────────────
        private readonly Random _rng = new Random();
        private int _lastChangeTick;
        private int _messageIntervalMs = 5000;

        /// <summary>Whether the bubble is currently visible.</summary>
        public bool IsActive { get; set; }

        /// <summary>The text currently displayed in the bubble.</summary>
        public string CurrentMessage { get; set; } = string.Empty;

        /// <summary>How high above the entity position to place the bubble (metres).</summary>
        public float HeightOffset { get; set; } = 1.2f;

        /// <summary>Message rotation interval in milliseconds.</summary>
        public int MessageIntervalMs
        {
            get => _messageIntervalMs;
            set => _messageIntervalMs = value;
        }

        /// <summary>Custom message pool.  Set to null to use <see cref="DefaultMessages"/>.</summary>
        public string[] CustomMessages { get; set; }

        // ── Public API ─────────────────────────────────────────────────

        /// <summary>Toggle the bubble on/off.  Picks a new message when turning on.</summary>
        public void Toggle()
        {
            IsActive = !IsActive;
            if (IsActive)
            {
                PickNewMessage();
            }
        }

        /// <summary>
        /// Call once per frame from <c>Script.Tick</c>.
        /// Projects the world position to screen and draws the bubble.
        /// </summary>
        /// <param name="worldPosition">
        /// World position above the entity (e.g. <c>ped.Position + (0,0,1.2)</c>).
        /// The caller supplies this so the controller is entity‑agnostic.
        /// </param>
        public void Update(Vector3 worldPosition)
        {
            if (!IsActive)
            {
                return;
            }

            // Rotate message
            if (Game.GameTime - _lastChangeTick > _messageIntervalMs)
            {
                PickNewMessage();
            }

            // World → screen (keeps on‑screen even behind camera)
            using (var outX = new OutputArgument())
            using (var outY = new OutputArgument())
            {
                Function.Call<int>(
                    (Hash)0xF9904D11F1ACBEC3,
                    worldPosition.X, worldPosition.Y, worldPosition.Z,
                    outX, outY);

                float sx = outX.GetResult<float>();
                float sy = outY.GetResult<float>();

                ChatBubble.Draw(sx, sy, CurrentMessage);
            }
        }

        /// <summary>Reset the bubble state (call from <c>Script.Aborted</c>).</summary>
        public void Reset()
        {
            IsActive = false;
            CurrentMessage = string.Empty;
        }

        // ── Internal ───────────────────────────────────────────────────

        private void PickNewMessage()
        {
            string[] pool = CustomMessages ?? DefaultMessages;
            CurrentMessage = pool[_rng.Next(pool.Length)];
            _lastChangeTick = Game.GameTime;
        }
    }
}