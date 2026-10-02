// Running-game captures for decision 0036 (owner feedback round 1). Run in Play mode in WorldGen. Isolated temp save; never the
// application's saves.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session;
using FoodFactoryGame.Session.Buildings;
using FoodFactoryGame.Session.Customers;
using FoodFactoryGame.Session.Equipment;
using FoodFactoryGame.Session.Player;
using UnityEngine;

public static class CaptureRestaurantFeedback
{
    const string Out = "docs/verification/restaurant-feedback-20261002";
    static readonly StringBuilder Log = new();

    static async Task Frames(int n) { for (var i = 0; i < n; i++) await Task.Yield(); }
    static async Task Seconds(float s) { var end = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < end) await Task.Yield(); }
    static async Task Until(Func<bool> p, string what, float timeout = 90f)
    {
        var end = Time.realtimeSinceStartup + timeout;
        while (!p() && Time.realtimeSinceStartup < end) await Task.Yield();
        if (!p()) throw new Exception("timed out: " + what);
    }

    static PlayerAvatar Avatar(SessionRoot root) => UnityEngine.Object.FindObjectsByType<PlayerAvatar>().FirstOrDefault(x => x.IsOwner && x.NetworkManager == root.NetworkManager);

    static void Capture(Camera camera, string name)
    {
        var texture = new RenderTexture(1600, 900, 24);
        var previous = camera.targetTexture;
        camera.targetTexture = texture;
        camera.Render();
        RenderTexture.active = texture;
        var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
        image.Apply();
        File.WriteAllBytes(Path.Combine(Out, name), image.EncodeToPNG());
        RenderTexture.active = null;
        camera.targetTexture = previous;
        UnityEngine.Object.Destroy(texture);
        UnityEngine.Object.Destroy(image);
        Log.AppendLine("captured " + name);
    }

    public static async Task<string> Run()
    {
        Directory.CreateDirectory(Out);
        var temp = Path.Combine(Path.GetTempPath(), "FoodFactoryCapture0036", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var root = UnityEngine.Object.FindAnyObjectByType<SessionRoot>();
        try
        {
            root.Configure(new SessionOptions { SaveDirectory = Path.Combine(temp, "save"), IdentityPath = Path.Combine(temp, "host.db"), DisplayName = "Host", Address = "127.0.0.1", WorldSeed = "piece-two" });
            if (!root.Begin(SessionMode.Host)) return "begin failed";
            await Until(() => root.ClientSite != null && root.ClientSubscription?.Bridge != null && Avatar(root) != null, "host");
            await Seconds(3f);
            var world = root.ServerWorld;
            var me = root.Authenticator.LocalPlayerId;
            var start = root.StartOffer;
            var site = start.SiteId;
            GoodsBuilding Shell() => world.Snapshot().Buildings.Single(x => x.SiteId == site);
            var shell = Shell();
            var layout = root.ClientSite.SiteLayouts.Single();
            Log.AppendLine($"lot {layout.Width}x{layout.Depth}, shell ({shell.CellX},{shell.CellZ}) {shell.Width}x{shell.Depth}, doors {string.Join(" ", shell.Doors.Select(d => $"({d.X},{d.Z})"))}, access ({start.AccessX},{start.AccessZ}) lot ({start.LotX},{start.LotZ})");

            var avatar = Avatar(root);
            var rig = avatar.CameraRig;
            var camera = rig.GetComponentInChildren<Camera>();
            var build = UnityEngine.Object.FindAnyObjectByType<BuildMode>();
            var interaction = UnityEngine.Object.FindAnyObjectByType<EquipmentInteraction>();

            // 1. Decor facing: a wall clock on the wall opposite the doors and on a side wall, a sink against a wall, both placed
            // through build mode with rotation 0 chosen.
            await Until(() => { if (!build.Active) interaction.OpenBuild(); return build.Active; }, "build open");
            await Frames(5);
            var doorZ = shell.Doors[0].Z;
            var backZ = doorZ == shell.CellZ ? shell.CellZ + shell.Depth - 1 : shell.CellZ;
            var clock = (X: shell.CellX + 3, Z: backZ);
            var clock2 = (X: shell.CellX, Z: shell.CellZ + shell.Depth / 2);
            var clock3 = (X: shell.CellX + 4, Z: doorZ == shell.CellZ ? shell.CellZ : shell.CellZ + shell.Depth - 1);
            if (shell.Doors.Any(d => d.X == clock3.X)) clock3.X += 3;
            foreach (var c in new[] { clock, clock2, clock3 })
            {
                build.SelectOffer("supplier-rt-wall-clock");
                build.PressAt(c); build.ReleaseAt(c);
                await Seconds(0.6f);
            }
            var inward = backZ == shell.CellZ ? 1 : -1;
            var sinkCell = (X: shell.CellX + 6, Z: backZ + inward);
            build.SelectOffer("supplier-rt-sink");
            build.PressAt(sinkCell); build.ReleaseAt(sinkCell);
            await Seconds(1f);
            foreach (var e in world.Snapshot().Equipment.Where(x => x.SiteId == site && (x.Kind == "rt-wall-clock" || x.Kind == "rt-sink")))
            {
                var (fx, fz) = SiteGrid.Facing(e.Rotation);
                Log.AppendLine($"{e.Kind} at ({e.CellX},{e.CellZ}) rotation {e.Rotation} faces ({fx},{fz}); front cell interior {SiteGrid.IsInterior(Shell(), e.CellX + fx, e.CellZ + fz)}; last rejection {build.LastRejection}");
            }

            // 2. Free walls: extend the back of the room by two rows outward if the lot allows, else open and re-close one wall cell.
            build.SelectTool(BuildTool.Wall, "brick");
            var outward = -inward;
            var newBack = backZ + 2 * outward;
            if (newBack >= 0 && newBack < layout.Depth)
            {
                var xs = (shell.CellX, shell.CellX + shell.Width - 1);
                build.Drag((xs.Item1, backZ + outward), (xs.Item1, newBack)); build.Confirm(); await Seconds(0.8f);
                build.Drag((xs.Item1 + 1, newBack), (xs.Item2, newBack)); build.Confirm(); await Seconds(0.8f);
                build.Drag((xs.Item2, backZ + outward), (xs.Item2, newBack - outward)); build.Confirm(); await Seconds(0.8f);
                Log.AppendLine($"extension walls: rejection {build.LastRejection}; interior {SiteGrid.InteriorCells(Shell()).Count()}");
                Capture(camera, "0036-build-extension-walls.png");
                // Take out the old back wall between the corners, except where decor hangs.
                var hung = world.Snapshot().Equipment.Where(e => e.SiteId == site && e.Layer == SiteGrid.WallLayer).Select(e => (e.CellX, e.CellZ)).ToHashSet();
                var removed = 0;
                for (var x = shell.CellX + 1; x < shell.CellX + shell.Width - 1; x++)
                {
                    if (hung.Contains((x, backZ))) continue;
                    build.RemoveAt((x, backZ));
                    await Seconds(0.3f);
                    removed++;
                }
                await Seconds(1f);
                var after = Shell();
                Log.AppendLine($"removed {removed} back wall cells; rejection {build.LastRejection}; footprint ({after.CellX},{after.CellZ}) {after.Width}x{after.Depth}; interior {SiteGrid.InteriorCells(after).Count()} (was {(shell.Width - 2) * (shell.Depth - 2)}); cash {world.Snapshot().Companies.Single(x => x.SiteIds.Contains(site)).Cash}");
            }
            else Log.AppendLine("no room behind the shell for the extension");

            // 3. Build camera: pan, zoom and tilt through the rig's state (input is driven by actions in play; here the fields).
            build.ClearSelection();
            Log.AppendLine($"after ClearSelection: tool {build.Tool}, offer {build.OfferId ?? "none"}");
            typeof(OrbitCameraRig).GetField("_buildPitch", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(rig, 50f);
            typeof(OrbitCameraRig).GetField("_buildYaw", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(rig, 30f);
            await Seconds(1.5f);
            Capture(camera, "0036-build-tilted.png");
            interaction.CloseScreen();
            await Seconds(1f);

            // 4. Inside, third-person: the ceiling shows and the camera stays in the room.
            var inside = SiteGrid.InteriorCells(Shell()).OrderBy(c => Mathf.Abs(c.X - (shell.CellX + shell.Width / 2)) + Mathf.Abs(c.Z - (shell.CellZ + shell.Depth / 2))).First();
            avatar.Teleport(SiteGridSpace.FootprintCenter(layout, inside.X, inside.Z, 1, 1) + Vector3.up * 0.1f);
            await Seconds(1.5f);
            var presenter = UnityEngine.Object.FindAnyObjectByType<BuildingPresenter>();
            Log.AppendLine($"inside: building {presenter.LocalBuildingId}, topDown {rig.TopDown}, ceiling {presenter.CeilingVisible(Shell().Id)}");
            Capture(camera, "0036-inside-topdown.png");
            typeof(OrbitCameraRig).GetMethod("SetTopDown", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(rig, new object[] { false });
            await Seconds(1.5f);
            var camPos = camera.transform.position;
            var camCell = SiteGridSpace.AnchorAt(layout, camPos, 1, 1);
            Log.AppendLine($"third person: ceiling {presenter.CeilingVisible(Shell().Id)}, arm {rig.CurrentArm:F2} of {rig.Distance}, camera height above floor {camPos.y - SiteGridSpace.FloorHeight(layout, 0):F2}, camera cell ({camCell.X},{camCell.Z}) interior {SiteGrid.IsInterior(Shell(), camCell.X, camCell.Z)}");
            Capture(camera, "0036-inside-third-person.png");

            // 5. A door: walk the avatar up to the front door from inside; the leaf opens.
            var swings = UnityEngine.Object.FindObjectsByType<DoorSwing>();
            var door = swings.OrderBy(s => (s.Doorway - avatar.transform.position).sqrMagnitude).First();
            Log.AppendLine($"{swings.Length} door leaves; nearest closed: angle {door.Angle:F0}, blocking {door.Blocking}");
            var dir = (door.Doorway - avatar.transform.position); dir.y = 0;
            avatar.Teleport(door.Doorway - dir.normalized * 1.2f + Vector3.up * 0.1f);
            await Seconds(1f);
            Log.AppendLine($"avatar 1.2 m away: angle {door.Angle:F0}, blocking {door.Blocking}");
            Capture(camera, "0036-door-open.png");
            avatar.Teleport(door.Doorway - dir.normalized * 4f + Vector3.up * 0.1f);
            await Seconds(1.5f);
            Log.AppendLine($"avatar 4 m away: angle {door.Angle:F0}, blocking {door.Blocking}");

            // 6. Customers: staff the register and watch figures for a while.
            var register = world.Snapshot().Equipment.First(x => x.SiteId == site && x.Kind == GoodsWorld.CounterKind);
            interaction.Staff(register.Id, me);
            avatar.Teleport(SiteGridSpace.FootprintCenter(layout, inside.X, inside.Z, 1, 1) + Vector3.up * 0.1f);
            typeof(OrbitCameraRig).GetMethod("SetTopDown", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(rig, new object[] { true });
            var walk = SiteWalk.For(root.ClientSite, layout);
            var spot = walk.ServiceSpot(root.ClientSite.Equipment.First(x => x.Id == register.Id));
            Log.AppendLine($"register ({register.CellX},{register.CellZ}) rot {register.Rotation}; service spot {spot}");
            var crossed = 0; var samples = 0;
            var endWatch = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < endWatch)
            {
                await Seconds(0.25f);
                foreach (var figure in UnityEngine.Object.FindObjectsByType<DoorOpener>().Where(x => x.name.StartsWith("Customer")))
                {
                    var p = figure.transform.position;
                    var (cx, cz) = SiteGridSpace.AnchorAt(layout, p, 1, 1);
                    samples++;
                    if (SiteGrid.IsWall(Shell(), cx, cz)) crossed++;
                }
            }
            Log.AppendLine($"customer figure samples {samples}, samples standing on a wall cell {crossed}; customers on site {root.ClientSite.Customers.Count(x => x.RestaurantId == site)}");
            Capture(camera, "0036-customers.png");
            return Log.ToString();
        }
        catch (Exception e)
        {
            return Log + "\nERROR " + e;
        }
        finally
        {
            File.WriteAllText(Path.Combine(Out, "log.txt"), Log.ToString());
            root.Shutdown();
        }
    }
}
