// The HUD's restaurant readout and ledger view (decision 0038, starting-loop P3). The readout says why the restaurant the player
// stands in can or cannot sell, from this client's baseline through RestaurantReadiness (the customer simulation's own rules),
// in a status card under the cash and in the register screen. The ledger view, a tab beside the Supplier on the inventory
// screen, lists the company's latest cash changes, newest first. Presentation only: both read the replicated snapshot and
// write nothing.
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
        private Label _registerReadiness;
        private GoodsSnapshot _readinessSite;
        private bool _ledgerShown;
        private List<RecipeDefinition> _menu;

        // The latest readout for the client's site, or null when it is not a restaurant (computed once per baseline).
        public RestaurantReadiness Readiness { get; private set; }
        public string ReadinessText => _readiness?.text ?? "";
        public bool LedgerShown => _ledgerShown;

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
                if (_registerReadiness != null) SetRegisterReadiness();
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
        private void FillRegisterReadiness(VisualElement window, bool register)
        {
            var line = window.Part<Label>("hud-register-readiness");
            if (!register)
            {
                line.RemoveFromHierarchy();
                return;
            }
            _registerReadiness = line;
            SetRegisterReadiness();
        }

        private void SetRegisterReadiness()
        {
            var readout = Readiness;
            _registerReadiness.EnableInClassList("hud-readiness-line--ready", readout is { Ready: true });
            _registerReadiness.EnableInClassList("hud-readiness-line--blocked", readout is { Ready: false });
            if (readout == null)
            {
                _registerReadiness.SetText("");
                return;
            }
            var first = readout.Ready ? $"Restaurant open: {readout.Queued} queued, {readout.Eating} eating" : $"Restaurant not selling: {BlockerText(readout)}";
            _registerReadiness.SetText(string.Join("\n", new[] { first }.Concat(readout.Warnings.Select(x => WarningText(readout, x)))));
        }

        // Shows the ledger instead of the Supplier on the inventory screen, like its tab. Public so tests drive the same path.
        public void ShowLedger(bool shown) => _ledgerShown = shown;

        // The Supplier and Ledger tabs, inserted under the header of the inventory screen's Supplier or Ledger window.
        private void AddTabs(VisualElement window)
        {
            var row = new VisualElement { name = "hud-tabs" };
            row.AddToClassList("ff-row");
            row.AddToClassList("hud-tabs");
            foreach (var (name, label, ledger) in new[] { ("hud-tab-supplier", "Supplier", false), ("hud-tab-ledger", "Ledger", true) })
            {
                // Not focusable: a focused button would click again on every keyboard Submit (Enter/Space).
                var tab = new Button(() => ShowLedger(ledger)) { name = name, text = label, focusable = false };
                tab.AddToClassList("ff-button");
                tab.AddToClassList("ff-chip");
                tab.EnableInClassList("ff-chip--chosen", _ledgerShown == ledger);
                tab.SetEnabled(_ledgerShown != ledger);
                row.Add(tab);
            }
            window.Insert(1, row);
        }

        // The company's latest ledger entries (decision 0038), newest first: clock time, kind, site and signed amount.
        private VisualElement LedgerWindow(GoodsSnapshot site)
        {
            var window = Window("hud-ledger", "Ledger");
            AddTabs(window);
            var company = site.Companies.FirstOrDefault();
            var entries = company == null ? new List<GoodsLedgerEntry>() : GoodsWorld.LedgerOf(site, company.Id).Take(LedgerRowsShown).ToList();
            if (entries.Count == 0) window.Add(Text("No cash changes recorded yet.", "ff-small"));
            foreach (var entry in entries)
            {
                var row = new VisualElement { name = $"hud-ledger-row-{entry.Id}", pickingMode = PickingMode.Ignore };
                row.AddToClassList("ff-row");
                row.AddToClassList("hud-ledger__row");
                row.Add(Text(FormatDuration(entry.ClockSeconds, false), "hud-ledger__cell", "hud-ledger__time", "ff-muted"));
                row.Add(Text(LedgerKind(entry.Kind), "hud-ledger__cell", "hud-ledger__kind"));
                row.Add(Text(site.Sites.FirstOrDefault(x => x.Id == entry.SiteId)?.Name ?? entry.SiteId, "hud-ledger__cell", "hud-ledger__site", "ff-muted", "ff-nowrap"));
                var amount = Text((entry.Cents > 0 ? "+" : "-") + FormatCash(System.Math.Abs(entry.Cents)), "hud-ledger__cell", "hud-ledger__amount");
                amount.EnableInClassList("ff-positive-text", entry.Cents > 0);
                row.Add(amount);
                window.Add(row);
            }
            if (company != null)
            {
                var totals = Text($"Opening {FormatCash(company.OpeningCents)}, earlier entries {FormatCash(company.LedgerCarriedCents)}, now {FormatCash(company.Cash)}",
                    "hud-note", "ff-small");
                totals.name = "hud-ledger-totals";
                window.Add(totals);
            }
            return window;
        }

        // A display-only label with style classes.
        private static Label Text(string text, params string[] classes)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            foreach (var name in classes) label.AddToClassList(name);
            return label;
        }

        private static string LedgerKind(string kind) => kind switch
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
            _ => kind
        };
    }
}
