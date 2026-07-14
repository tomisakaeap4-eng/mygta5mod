using GTA;
using GTA.Math;
using System;
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
            if (e.KeyCode == Keys.F5)
            {
                _playerBubble.Toggle();
            }
        }

        private void OnAborted(object sender, EventArgs e)
        {
            _playerBubble.Reset();
        }
    }
}