using GTA;
using GTA.Native;
using System;
using System.Collections.Generic;

namespace FirstLegacyMod
{
    /// <summary>
    /// Pure UI renderer for the chat bubble.
    /// Draws a speech-bubble at normalised screen coordinates (0‑1)
    /// with dynamic width, dynamic height (word wrap), soft shadow,
    /// and a triangular tail. Supports multi‑line AI chat text.
    ///
    /// Works around the 99‑character limit of
    /// ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME by manually wrapping
    /// text into short lines and drawing each line separately.
    /// </summary>
    public static class ChatBubble
    {
        // ── Layout constants ────────────────────────────────────────────
        private const float MinWidth    = 0.12f;
        private const float MaxWidth    = 0.52f;
        private const float MaxBubbleH  = 0.55f;
        private const float PaddingX    = 0.026f;
        private const float PaddingY    = 0.018f;
        private const float LineHeight  = 0.034f;
        private const float MinBubbleH  = 0.052f;
        private const float YOffset     = 0.0f;

        // ── Tail ────────────────────────────────────────────────────────
        private const float TailStep    = 0.0055f;

        // ── Colours ─────────────────────────────────────────────────────
        private static readonly (int r, int g, int b, int a) ShadowColour
            = (0, 0, 0, 90);
        private static readonly (int r, int g, int b, int a) BubbleColour
            = (255, 255, 255, 240);
        private static readonly (int r, int g, int b, int a) TextColour
            = (15, 15, 15, 255);

        /// <summary>
        /// Approximate chars that fit within MaxWidth at the current text
        /// scale (0.38).  ~55 characters per line.
        /// </summary>
        private const int CharsPerLine = 55;

        // ════════════════════════════════════════════════════════════════
        //  Public API
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Draw the chat bubble at the given normalised screen position.
        /// </summary>
        public static void Draw(float x, float y, string message)
        {
            // ── Wrap text manually ──────────────────────────────────
            List<string> lines = WrapText(message, CharsPerLine);
            int lineCount = lines.Count;
            if (lineCount < 1) return;

            // ── Dynamic sizes ───────────────────────────────────────
            float bubbleW = Clamp(CharsPerLine * 0.0068f + PaddingX * 2f, MinWidth, MaxWidth);
            float bubbleH = Clamp(lineCount * LineHeight + PaddingY * 2f, MinBubbleH, MaxBubbleH);

            // Anchor bottom: bubble sits above the entity, expands upward
            float bubbleBottom = y + YOffset;   // fixed — tail connects here
            float centerY = bubbleBottom - bubbleH * 0.5f;
            float bubbleTop = bubbleBottom - bubbleH;

            // ── Soft shadow (2 layers) ──────────────────────────────
            DrawRect(x + 0.003f, centerY + 0.003f,
                     bubbleW + 0.008f, bubbleH + 0.008f, ShadowColour);
            DrawRect(x + 0.0015f, centerY + 0.0015f,
                     bubbleW + 0.004f, bubbleH + 0.004f, ShadowColour);

            // ── Bubble body ─────────────────────────────────────────
            DrawRect(x, centerY, bubbleW, bubbleH, BubbleColour);

            // ── Tail (points down from bubble bottom to entity) ─────
            DrawTail(x, bubbleBottom, BubbleColour);

            // ── Draw each line (top → bottom) ───────────────────────
            float startY = bubbleTop + PaddingY;
            for (int i = 0; i < lineCount; i++)
            {
                float lineY = startY + i * LineHeight;

                Function.Call(Hash.SET_TEXT_FONT, 0);
                Function.Call(Hash.SET_TEXT_SCALE, 0.0f, 0.38f);
                Function.Call(Hash.SET_TEXT_COLOUR,
                    TextColour.r, TextColour.g, TextColour.b, TextColour.a);
                Function.Call(Hash.SET_TEXT_CENTRE, true);
                Function.Call(Hash.SET_TEXT_DROP_SHADOW, 0, 0, 0, 0, 0);

                Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
                Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, lines[i]);
                Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, x, lineY);
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  Helpers
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Wraps a string into lines, preserving explicit newlines and
        /// breaking long words.  Every returned line is ≤ maxChars.
        /// </summary>
        private static List<string> WrapText(string text, int maxChars)
        {
            var result = new List<string>();

            // Split on explicit newlines first
            string[] paragraphs = text.Split(new[] { "\r\n", "\r", "\n" },
                                             StringSplitOptions.None);

            foreach (string paragraph in paragraphs)
            {
                if (string.IsNullOrEmpty(paragraph))
                {
                    result.Add(string.Empty); // preserve blank line
                    continue;
                }

                string[] words = paragraph.Split(' ');
                string currentLine = string.Empty;

                foreach (string word in words)
                {
                    if (string.IsNullOrEmpty(word))
                        continue;

                    // If a single word is longer than maxChars, split it
                    if (word.Length > maxChars)
                    {
                        // Flush current line first
                        if (currentLine.Length > 0)
                        {
                            result.Add(currentLine.TrimEnd());
                            currentLine = string.Empty;
                        }

                        // Split the long word across multiple lines
                        for (int i = 0; i < word.Length; i += maxChars)
                        {
                            int len = Math.Min(maxChars, word.Length - i);
                            result.Add(word.Substring(i, len));
                        }
                        continue;
                    }

                    string test = currentLine.Length == 0
                        ? word
                        : currentLine + " " + word;

                    if (test.Length > maxChars)
                    {
                        result.Add(currentLine.TrimEnd());
                        currentLine = word;
                    }
                    else
                    {
                        currentLine = test;
                    }
                }

                if (currentLine.Length > 0)
                {
                    result.Add(currentLine.TrimEnd());
                }
            }

            return result;
        }

        private static void DrawTail(
            float centerX, float topY,
            (int r, int g, int b, int a) colour)
        {
            float[] widths = { 0.018f, 0.013f, 0.008f, 0.004f };
            for (int i = 0; i < widths.Length; i++)
            {
                DrawRect(centerX,
                         topY + TailStep * (i + 0.5f),
                         widths[i], TailStep, colour);
            }
        }

        private static void DrawRect(
            float x, float y, float w, float h,
            (int r, int g, int b, int a) colour)
        {
            Function.Call(Hash.DRAW_RECT,
                x, y, w, h,
                colour.r, colour.g, colour.b, colour.a);
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}