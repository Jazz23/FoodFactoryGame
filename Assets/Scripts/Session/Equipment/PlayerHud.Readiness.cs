// The HUD's restaurant readout and ledger view (decision 0038, starting-loop P3). The readout says why the restaurant the player
// stands in can or cannot sell, from this client's baseline through RestaurantReadiness (the customer simulation's own rules),
// in the world view under the cash and in the register screen. The ledger view, a tab beside the Supplier on the inventory
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
        private static readonly Color Ready = new(0.6f, 0.9f, 0.55f, 1f);

        private Label _readiness;
        private Label _registerReadiness;
        private GoodsSnapshot _readinessSite;
        private bool _ledgerShown;
        private List<RecipeDefinition> _menu;

        // The latest readout for the client's site, or null when it is not a restaurant (computed once per baseline).
        public RestaurantReadiness Readiness { get; private set; }
        public string ReadinessText => _readiness?.text ?? "";
        public bool LedgerShown => _ledgerShown;

        private void CreateReadiness(VisualElement layer)
        {
            _readiness = Caption("", 13, Color.white);
            _readiness.name = "hud-readiness";
            _readiness.style.position = Position.Absolute;
            _readiness.style.top = 40;
            _readiness.style.right = 16;
            _readiness.style.maxWidth = 560;
            _readiness.style.unityTextAlign = TextAnchor.UpperRight;
            _readiness.style.textShadow = new TextShadow { offset = new Vector2(1f, 1f), color = Color.black };
            _readiness.style.backgroundColor = new Color(0f, 0f, 0f, 0.35f);
            _readiness.style.paddingLeft = _readiness.style.paddingRight = 6;
            _readiness.style.paddingTop = _readiness.style.paddingBottom = 3;
            layer.Add(_readiness);
        }

        // Once per baseline: the readout of the site this client stands in; the world line shows only with no screen open.
        private void UpdateReadiness(GoodsSnapshot site, bool active)
        {
            if (!ReferenceEquals(site, _readinessSite))
            {
                _readinessSite = site;
                var siteId = interaction.Session.ClientSiteId;
                var lot = SitePlacement.Active?.LotOf(siteId);
                _menu ??= interaction.Session.Recipes.Where(x => x != null).Select(x => x.ToDefinition()).Where(RestaurantRules.IsMenuItem).ToList();
                Readiness = active ? RestaurantReadiness.Evaluate(site, siteId, lot == null ? null : SitePlacement.Active.OfferOf(lot.Id), _menu) : null;
                _readiness.text = Readiness == null ? "" : WorldText(Readiness);
                _readiness.style.color = Readiness is { Ready: false } ? Spoiled : Color.white;
                if (_registerReadiness != null) SetRegisterReadiness();
            }
            _readiness.style.display = active && Readiness != null && interaction.Screen == InteractionScreen.None ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // The world line: what stops sales (or that the restaurant is open), the warnings, then the recent ledger.
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

        private string MenuFood() =>
            string.Join(" or ", (_menu ?? new List<RecipeDefinition>()).SelectMany(x => x.Inputs).Select(x => ItemName(x.ItemId)).Distinct().DefaultIfEmpty("food"));

        // The register screen's line: the same readout, blocker first, else open, then the warnings.
        private VisualElement RegisterReadiness()
        {
            _registerReadiness = Caption("", 12, Color.white, 6);
            _registerReadiness.name = "hud-register-readiness";
            _registerReadiness.style.maxWidth = 420;
            SetRegisterReadiness();
            return _registerReadiness;
        }

        private void SetRegisterReadiness()
        {
            var readout = Readiness;
            if (readout == null)
            {
                _registerReadiness.text = "";
                return;
            }
            var first = readout.Ready ? $"Restaurant open: {readout.Queued} queued, {readout.Eating} eating" : $"Restaurant not selling: {BlockerText(readout)}";
            _registerReadiness.text = string.Join("\n", new[] { first }.Concat(readout.Warnings.Select(x => WarningText(readout, x))));
            _registerReadiness.style.color = readout.Ready ? Ready : Spoiled;
        }

        // Shows the ledger instead of the Supplier on the inventory screen, like its tab. Public so tests drive the same path.
        public void ShowLedger(bool shown) => _ledgerShown = shown;

        // The Supplier and Ledger tabs heading the inventory screen's right-hand window.
        private VisualElement Tabs()
        {
            var row = new VisualElement { name = "hud-tabs" };
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 4;
            foreach (var (name, label, ledger) in new[] { ("hud-tab-supplier", "Supplier", false), ("hud-tab-ledger", "Ledger", true) })
            {
                // Not focusable: a focused button would click again on every keyboard Submit (Enter/Space).
                var tab = new Button(() => ShowLedger(ledger)) { name = name, text = label, focusable = false };
                tab.SetEnabled(_ledgerShown != ledger);
                row.Add(tab);
            }
            return row;
        }

        // The company's latest ledger entries (decision 0038), newest first: clock time, kind, site and signed amount.
        private VisualElement LedgerWindow(GoodsSnapshot site)
        {
            var window = Window("hud-ledger", "Ledger");
            window.Insert(0, Tabs());
            var company = site.Companies.FirstOrDefault();
            var entries = company == null ? new List<GoodsLedgerEntry>() : GoodsWorld.LedgerOf(site, company.Id).Take(LedgerRowsShown).ToList();
            if (entries.Count == 0) window.Add(Caption("No cash changes recorded yet.", 12, Muted));
            foreach (var entry in entries)
            {
                var row = new VisualElement { name = $"hud-ledger-row-{entry.Id}" };
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginTop = 2;
                void Cell(string text, int width, Color color, TextAnchor align = TextAnchor.MiddleLeft)
                {
                    var cell = Caption(text, 12, color);
                    cell.style.width = width;
                    cell.style.unityTextAlign = align;
                    row.Add(cell);
                }
                Cell(FormatDuration(entry.ClockSeconds, false), 70, Muted);
                Cell(LedgerKind(entry.Kind), 130, Color.white);
                Cell(site.Sites.FirstOrDefault(x => x.Id == entry.SiteId)?.Name ?? entry.SiteId, 120, Muted);
                Cell((entry.Cents > 0 ? "+" : "-") + FormatCash(System.Math.Abs(entry.Cents)), 90, entry.Cents > 0 ? Ready : Color.white, TextAnchor.MiddleRight);
                window.Add(row);
            }
            if (company != null)
            {
                var opening = Caption($"Opening {FormatCash(company.OpeningCents)}, earlier entries {FormatCash(company.LedgerCarriedCents)}, now {FormatCash(company.Cash)}",
                    11, Muted, 6);
                opening.name = "hud-ledger-totals";
                window.Add(opening);
            }
            return window;
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
            _ => kind
        };
    }
}
