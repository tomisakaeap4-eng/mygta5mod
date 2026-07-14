using GTA;
using LemonUI;
using LemonUI.Menus;
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace FirstLegacyMod
{
    public sealed class MainScript : Script
    {
        private readonly ObjectPool _pool = new ObjectPool();

        private readonly NativeMenu _mainMenu =
            new NativeMenu("FIRST MOD", "Main Menu");

        private readonly NativeItem _spawnVehicleItem =
            new NativeItem(
                "Spawn Zentorno",
                "Creates a Zentorno in front of the player."
            );

        private readonly NativeCheckboxItem _invincibleItem =
            new NativeCheckboxItem(
                "Invincible",
                "Enable or disable player invincibility.",
                false
            );

        private readonly NativeItem _closeMenuItem =
            new NativeItem("Close Menu");

        private readonly List<Vehicle> _spawnedVehicles =
            new List<Vehicle>();

        public MainScript()
        {
            _mainMenu.Add(_spawnVehicleItem);
            _mainMenu.Add(_invincibleItem);
            _mainMenu.Add(_closeMenuItem);

            // Every LemonUI menu must be registered once.
            _pool.Add(_mainMenu);

            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += OnAborted;

            _spawnVehicleItem.Activated += OnSpawnVehicleActivated;
            _invincibleItem.CheckboxChanged += OnInvincibleChanged;
            _closeMenuItem.Activated += OnCloseMenuActivated;
        }

        private void OnTick(object sender, EventArgs e)
        {
            // Required for LemonUI drawing and input processing.
            _pool.Process();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.F5)
            {
                return;
            }

            if (_pool.AreAnyVisible)
            {
                _pool.HideAll();
            }
            else
            {
                _mainMenu.Visible = true;
            }
        }

        private void OnSpawnVehicleActivated(object sender, EventArgs e)
        {
            Ped player = Game.Player.Character;

            if (player == null || !player.Exists())
            {
                return;
            }

            Vehicle vehicle = World.CreateVehicle(
                VehicleHash.Zentorno,
                player.Position + player.ForwardVector * 5.0f,
                player.Heading
            );

            if (vehicle == null || !vehicle.Exists())
            {
                return;
            }

            vehicle.PlaceOnGround();
            vehicle.Mods.LicensePlate = "LEMONUI";

            _spawnedVehicles.Add(vehicle);
        }

        private void OnInvincibleChanged(object sender, EventArgs e)
        {
            Ped player = Game.Player.Character;

            if (player == null || !player.Exists())
            {
                return;
            }

            player.IsInvincible = _invincibleItem.Checked;
        }

        private void OnCloseMenuActivated(object sender, EventArgs e)
        {
            _pool.HideAll();
        }

        private void OnAborted(object sender, EventArgs e)
        {
            _pool.HideAll();

            Ped player = Game.Player.Character;

            if (player != null && player.Exists())
            {
                player.IsInvincible = false;
            }

            foreach (Vehicle vehicle in _spawnedVehicles)
            {
                if (vehicle != null && vehicle.Exists())
                {
                    vehicle.Delete();
                }
            }

            _spawnedVehicles.Clear();
        }
    }
}