using GTA;
using GTA.Math;
using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FirstLegacyMod
{
    public sealed class MainScript : Script
    {
        private readonly ChatBubbleController _playerBubble = new ChatBubbleController();

        // ── AI state ──────────────────────────────────────────────
        private Task<string> _pendingAiTask;
        private volatile bool _isAiRequestPending;

        public MainScript()
        {
            // Initialize AI service (reads OPENAI_API_KEY + OPENAI_BASE_URL)
            AIChatService.Initialize();

            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += OnAborted;
        }

        private void OnTick(object sender, EventArgs e)
        {
            // ── Check for completed AI response ──────────────────
            if (_isAiRequestPending && _pendingAiTask != null && _pendingAiTask.IsCompleted)
            {
                string response;
                try
                {
                    response = _pendingAiTask.Result;
                }
                catch (AggregateException ex)
                {
                    response = $"[AI Lỗi] {ex.InnerException?.Message ?? ex.Message}";
                }

                _playerBubble.SetFixedMessage(response);
                _isAiRequestPending = false;
                _pendingAiTask = null;
            }

            // ── Render bubble if active ───────────────────────────
            if (!_playerBubble.IsActive)
            {
                return;
            }

            Ped player = Game.Player.Character;
            if (player == null || !player.Exists())
            {
                return;
            }

            Vector3 headWorld = player.Position + new Vector3(0f, 0f, _playerBubble.HeightOffset);
            _playerBubble.Update(headWorld);
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            // ── T: Request AI Vietnamese response ─────────────────
            if (e.KeyCode == Keys.T)
            {
                if (_isAiRequestPending)
                {
                    return; // Already waiting for a response
                }

                if (!AIChatService.IsAvailable)
                {
                    // Show error message if AI not available
                    _playerBubble.SetFixedMessage(
                        "[AI] Chưa cấu hình API Key.\n" +
                        "Đặt biến môi trường OPENAI_API_KEY.");
                    return;
                }

                _isAiRequestPending = true;

                // Show waiting bubble immediately with live elapsed-time counter
                _playerBubble.StartWaiting();

                // Fire-and-forget: run API call on background thread
                _pendingAiTask = Task.Run(() => AIChatService.GetVietnameseResponse());
            }

            // ── Esc: Dismiss bubble ───────────────────────────────
            if (e.KeyCode == Keys.Escape)
            {
                _playerBubble.Reset();
                _isAiRequestPending = false;
                _pendingAiTask = null;
            }
        }

        private void OnAborted(object sender, EventArgs e)
        {
            _playerBubble.Reset();
            _isAiRequestPending = false;
            _pendingAiTask = null;
        }
    }
}
