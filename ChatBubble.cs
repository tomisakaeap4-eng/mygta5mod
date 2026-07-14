using GTA.Native;

namespace FirstLegacyMod
{
    /// <summary>
    /// Pure UI renderer for the chat bubble.
    /// Draws a speech-bubble at normalised screen coordinates (0‑1).
    /// </summary>
    public static class ChatBubble
    {
        // ── Appearance constants ───────────────────────────────────────
        private const float BubbleW = 0.15f;
        private const float BubbleH = 0.04f;
        private const float TailH = 0.006f;
        private const float TailW = 0.012f;
        private const float YOffset = -0.005f;

        /// <summary>
        /// Draw the chat bubble at the given normalised screen position.
        /// </summary>
        /// <param name="x">Normalised screen X (0‑1).</param>
        /// <param name="y">Normalised screen Y (0‑1).</param>
        /// <param name="message">Text to display inside the bubble.</param>
        public static void Draw(float x, float y, string message)
        {
            // Border (dark outline behind the white rect)
            Function.Call(Hash.DRAW_RECT,
                x, y + YOffset,
                BubbleW + 0.004f, BubbleH + 0.004f,
                30, 30, 30, 230);

            // Bubble background
            Function.Call(Hash.DRAW_RECT,
                x, y + YOffset,
                BubbleW, BubbleH,
                255, 255, 255, 220);

            // Tail (small pointer towards the entity)
            Function.Call(Hash.DRAW_RECT,
                x, y + YOffset + (BubbleH * 0.5f) + (TailH * 0.5f),
                TailW, TailH,
                255, 255, 255, 220);

            // Text setup
            Function.Call(Hash.SET_TEXT_FONT, 0);
            Function.Call(Hash.SET_TEXT_SCALE, 0.0f, 0.35f);
            Function.Call(Hash.SET_TEXT_COLOUR, 0, 0, 0, 255);
            Function.Call(Hash.SET_TEXT_CENTRE, true);
            Function.Call(Hash.SET_TEXT_DROP_SHADOW, 0, 0, 0, 0, 0);

            // Draw text centred inside the bubble
            Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
            Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, message);
            Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT,
                x, y + YOffset - 0.017f);
        }
    }
}