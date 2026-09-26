// Starts an isolated host and seeds visual-check-only queued customers for a running-game capture.
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session;
using FoodFactoryGame.Session.Customers;
using UnityEngine;

public static class CheckCustomerVisuals
{
    public static string Start()
    {
        var root = UnityEngine.Object.FindAnyObjectByType<SessionRoot>();
        if (root == null || root.IsRunning) throw new InvalidOperationException("DevSite must be playing with no session.");
        var directory = Path.Combine(Path.GetTempPath(), "FoodFactoryCustomerVisualCheck", Guid.NewGuid().ToString("N"));
        root.Configure(new SessionOptions
        {
            SaveDirectory = Path.Combine(directory, "save"), IdentityPath = Path.Combine(directory, "host.db"),
            DisplayName = "Visual Check", Address = "127.0.0.1"
        });
        using (var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            root.NetworkManager.TransportManager.Transport.SetPort((ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port);
        if (!root.Begin(SessionMode.Host)) throw new InvalidOperationException("Could not start isolated host: " + root.Status);
        return $"Isolated host started at {directory}. Seed after ServerBridge.IsServing. Camera={Camera.main?.transform.position}";
    }

    public static string Seed()
    {
        var root = UnityEngine.Object.FindAnyObjectByType<SessionRoot>();
        if (root?.ServerBridge == null || !root.ServerBridge.IsServing)
            throw new InvalidOperationException("Bridge must be serving before adding visual-check customers.");
        var recipe = root.Recipes.First(x => x != null && x.IsSale && x.StationKind == GoodsWorld.CounterKind);
        for (var index = 0; index < 12; index++)
            root.ServerWorld.Bootstrap(new GoodsCustomer
            {
                Id = "visual-check-" + index, DistrictId = DevWorld.DistrictId, Appearance = index % 4,
                DineIn = index % 3 != 0, PatienceSeconds = 600, State = CustomerState.Queued,
                RestaurantId = DevWorld.SiteId, RecipeId = recipe.Id, Ticket = 1000 + index
            });
        return "Queued 12 visual-check customers in the isolated host.";
    }

    public static string SeedMore(int total)
    {
        var root = UnityEngine.Object.FindAnyObjectByType<SessionRoot>();
        if (root?.ServerBridge == null || !root.ServerBridge.IsServing)
            throw new InvalidOperationException("Bridge must be serving before adding visual-check customers.");
        var recipe = root.Recipes.First(x => x != null && x.IsSale && x.StationKind == GoodsWorld.CounterKind);
        for (var index = 12; index < total; index++)
            root.ServerWorld.Bootstrap(new GoodsCustomer
            {
                Id = "visual-check-" + index, DistrictId = DevWorld.DistrictId, Appearance = index % 4,
                DineIn = index % 3 != 0, PatienceSeconds = 600, State = CustomerState.Queued,
                RestaurantId = DevWorld.SiteId, RecipeId = recipe.Id, Ticket = 1000 + index
            });
        return $"Queued {total} visual-check customers in the isolated host.";
    }

    public static string Inspect()
    {
        var root = UnityEngine.Object.FindAnyObjectByType<SessionRoot>();
        var presenter = UnityEngine.Object.FindAnyObjectByType<CustomerPresenter>();
        var site = root.ClientSite;
        var shown = site?.Customers.Count ?? -1;
        var camera = Camera.main;
        var path = new UnityEngine.AI.NavMeshPath();
        var hasPath = UnityEngine.AI.NavMesh.CalculatePath(new Vector3(0, 0, 8), new Vector3(-3, 0, 3), UnityEngine.AI.NavMesh.AllAreas, path);
        return $"status={root.Status} siteCustomers={shown} visible={presenter.VisibleCount} " +
            $"bridge={root.ServerBridge?.IsServing} camera={camera?.transform.position} " +
            $"navPath={hasPath}/{path.status} corners={path.corners.Length}";
    }
}
