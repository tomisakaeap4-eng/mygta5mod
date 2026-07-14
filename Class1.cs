using System;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.UI;

public sealed class FirstGtaMod : Script
{
    public FirstGtaMod()
    {
        // Đăng ký sự kiện bàn phím.
        KeyDown += OnKeyDown;

        // Hiện thông báo khi script được load.
        Notification.Show(
            "~g~FirstGtaMod loaded~s~. Press F6 to spawn a Zentorno."
        );
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        // Chỉ thực hiện khi người chơi nhấn F6.
        if (e.KeyCode != Keys.F6)
        {
            return;
        }

        Ped player = Game.Player.Character;

        // Tính vị trí cách nhân vật 5 mét về phía trước.
        Vector3 spawnPosition =
            player.Position + player.ForwardVector * 5.0f;

        // Tạo xe Zentorno.
        Vehicle vehicle = World.CreateVehicle(
            VehicleHash.Zentorno,
            spawnPosition,
            player.Heading
        );

        // World.CreateVehicle có thể trả về null nếu không tạo được xe.
        if (vehicle == null)
        {
            Notification.Show("~r~Could not spawn the vehicle.");
            return;
        }

        // Đặt xe đúng trên mặt đất.
        vehicle.PlaceOnGround();

        // Biển số tối đa 8 ký tự.
        vehicle.Mods.LicensePlate = "FIRSTMOD";

        Notification.Show("~g~Zentorno spawned successfully.");
    }
}