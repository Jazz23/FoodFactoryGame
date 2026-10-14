// The HUD's restaurant readout (decision 0038, starting-loop P3): why the restaurant the player stands in can or cannot sell,
// from this client's baseline through RestaurantReadiness (the customer simulation's own rules), in a status card under the
// cash (the register screen carries no such text, owner 2026-10-13). The ledger itself is the phone's Ledger app
// (PhonePanel); its row count and kind names are shared from here. Presentation only: reads the replicated snapshot and writes
// nothing.
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using UnityEngine;
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session.Equipment
{
    public sealed partial class PlayerHud
    {
        // PROTOTYPE (decision 0038): ledger rows shown, newest first (every kept entry is replicated).
        public const int LedgerRowsShown = 20;

        private Label _readiness;
        private GoodsSnapshot _readinessSite;
        private List<RecipeDefinition> _menu;

        // The latest readout for the client's site, or null when it is not a restaurant (computed once per baseline).
        public RestaurantReadiness Readiness { get; private set; }
        public string ReadinessText => _readiness?.text ?? "";

        // The readout card in Hud.uxml's status column, under the cash and the wage warning.
        private void CreateReadiness(VisualElement root)
        {
            _readiness = root.Part<Label>("hud-readiness");
            _readiness.Show(false);
        }

        // Once per baseline: the readout of the site this client stands in; the world card shows only with no screen open.
        private void UpdateReadiness(GoodsSnapshot site, bool active)
        {
            if (!ReferenceEquals(site, _readinessSite))
            {
                _readinessSite = site;
                var siteId = interaction.Session.ClientSiteId;
                var lot = SitePlacement.Active?.LotOf(siteId);
                _menu ??= interaction.Session.Recipes.Where(x => x != null).Select(x => x.ToDefinition()).Where(RestaurantRules.IsMenuItem).ToList();
                Readiness = active ? RestaurantReadiness.Evaluate(site, siteId, lot == null ? null : SitePlacement.Active.OfferOf(lot.Id), _menu) : null;
                _readiness.SetText(Readiness == null ? "" : WorldText(Readiness));
                _readiness.EnableInClassList("hud-readiness--blocked", Readiness is { Ready: false });
            }
            _readiness.Show(active && Readiness != null && interaction.Screen == InteractionScreen.None);
        }

        // The world card: what stops sales (or that the restaurant is open), the warnings, then the recent ledger.
        private string WorldText(RestaurantReadiness readout)
        {
            var lines = new List<string> { readout.Ready ? $"Open: {readout.Queued} queued, {readout.Eating} eating" : $"Not selling: {BlockerText(readout)}" };
            if (readout.Warnings.Count > 0) lines.Add(string.Join("  ·  ", readout.Warnings.Select(x => WarningText(readout, x))));
            lines.Add($"Last {FormatDuration(RestaurantReadiness.RecentSeconds, true)}: {readout.RecentSales} sale{(readout.RecentSales == 1 ? "" : "s")} "
                + $"{FormatCash(readout.RecentSalesCents)}, spent {FormatCash(readout.RecentSpendCents)}");
            return string.Join("\n", lines);
        }

        private string BlockerText(RestaurantReadiness readout) => readout.Blocker switch
        {
            RestaurantReadiness.NoCustomerDoor => "the restaurant has no customer door (add a door in build mode, B)",
            RestaurantReadiness.NoReachableRegister when readout.Registers == 0 => "no register (buy one in build mode, B)",
            RestaurantReadiness.NoReachableRegister => "customers cannot reach a register from the front door",
            RestaurantReadiness.NoStaffedRegister => "nobody works a register (open it and choose Work this register)",
            RestaurantReadiness.NoEdibleMenuItem => $"no {MenuFood()} in a worked register (put some in its input)",
            _ => readout.Blocker
        };

        private string WarningText(RestaurantReadiness readout, string warning) => warning switch
        {
            RestaurantReadiness.NoReachableSeat => "takeaway only: no seat customers can reach",
            RestaurantReadiness.DockNotBesideBackDoor => "a dock is not beside a back door",
            RestaurantReadiness.StockSpoilsSoon => $"{ItemName(readout.SpoilingItemId)} in a register spoils in {FormatDuration(readout.SpoilingSeconds, false)}",
            _ => warning
        };

        // The menu's foods by name while there are a few; a full kitchen menu (decision 0043) reads as "menu food".
        private string MenuFood()
        {
            var foods = (_menu ?? new List<RecipeDefinition>()).SelectMany(x => x.Inputs).Select(x => ItemName(x.ItemId)).Distinct().ToList();
            return foods.Count == 0 ? "food" : foods.Count <= 3 ? string.Join(" or ", foods) : "menu food";
        }

        // The register screen's line (HudMachineWindow's hud-register-readiness): the same readout, blocker first, else open, then
        // the warnings. Other machines drop the line.
        // A ledger entry's kind as shown to players.
        public static string LedgerKind(string kind) => kind switch
        {
            GoodsWorld.LedgerSale => "Sale",
            GoodsWorld.LedgerSupplierGoods => "Supplier goods",
            GoodsWorld.LedgerSupplierEquipment => "Supplier equipment",
            GoodsWorld.LedgerTruck => "Truck",
            GoodsWorld.LedgerProperty => "Property",
            GoodsWorld.LedgerFloor => "Factory floor",
            GoodsWorld.LedgerFurnish => "Build: furnishing",
            GoodsWorld.LedgerSellBack => "Sold back",
            GoodsWorld.LedgerShell => "Build: walls and doors",
            GoodsWorld.LedgerAdjustment => "Adjustment",
            GoodsWorld.LedgerWage => "Wages",
            GoodsWorld.LedgerFarmGoods => "Farm goods",
            GoodsWorld.LedgerFarmDelivery => "Farm delivery",
            GoodsWorld.LedgerFarmRefund => "Farm refund",
            _ => kind
        };
    }
}
