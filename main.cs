using GTA;
using GTA.Math;
using System;
using System.Threading;
using System.Windows.Forms;

namespace FirstLegacyMod
{
    public sealed class MainScript : Script
    {
        private readonly ChatBubbleController _playerBubble = new ChatBubbleController();

        public MainScript()
        {
            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += OnAborted;
        }

        private void OnTick(object sender, EventArgs e)
        {
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
            if (e.KeyCode == Keys.T)
            {
                string input = ReadClipboardUnicode();
                if (!string.IsNullOrWhiteSpace(input))
                {
                    _playerBubble.SetFixedMessage(input.Trim());
                }
            }

            if (e.KeyCode == Keys.Escape)
            {
                _playerBubble.Reset();
            }
        }

        /// <summary>
        /// Reads clipboard text on an STA thread (required by Windows Forms
        /// <see cref="Clipboard"/>). Returns the clipboard string or empty.
        /// </summary>
        private static string ReadClipboardUnicode()
        {
            string result = string.Empty;
            var thread = new Thread(() =>
            {
                try
                {
                    result = Clipboard.GetText();
                }
                catch
                {
                    // Clipboard may be locked; ignore
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(1000); // 1s timeout to avoid hanging
            return result;
        }

        private void OnAborted(object sender, EventArgs e)
        {
            _playerBubble.Reset();
        }
    }
}