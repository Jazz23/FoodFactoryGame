// Build mode's single catalog (owner, 2026-10-14): walls, doors, back doors, windows and wall finishes (one card per style the
// server accepts) are listed beside the supplier's equipment and the belt packs, grouped into filter categories (All shows every
// card). Presentation only: a card selects a BuildMode tool, style or offer, and every order it leads to is still planned and
// validated by the server (GoodsWorld.PlanShell, FurnishProblem). Shown prices are the base list prices at the site's district
// price percentage; the preview line shows the exact charge, including the shell order fee.
using System;
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using UnityEngine;

namespace FoodFactoryGame.Session.Buildings
{
    // Catalog filter chips, in display order; All is the default.
    public enum BuildCatalogFilter
    {
        All,
        Structure,
        Kitchen,
        Storage,
        Sales,
        Furniture,
        Decor,
        Logistics
    }

    public sealed class BuildCatalogEntry
    {
        // Unique among the cards; the card element is named build-item-<Key>.
        public string Key;
        public BuildCatalogFilter Filter;
        // The heading the card is listed under.
        public string Section;
        public string Title;
        public long PriceCents;
        // Appended to the price (" / m" for walls); a zero price reads "Free".
        public string PriceSuffix = "";
        public Sprite Icon;
        // Wall, Door, BackDoor, Window or Finish with Style for a structure card; Item with OfferId for equipment; None with
        // OfferId for a belt pack (its card hands over to the belt cursor).
        public BuildTool Tool;
        public string Style = "";
        public string OfferId;
        public bool IsBelt => Tool == BuildTool.None && OfferId != null;
    }

    public static class BuildCatalog
    {
        public const string BeltSection = "Belts";
        // Heading order inside the catalog; a section not listed here follows, in the order it first appears.
        private static readonly string[] SectionOrder =
        {
            "Walls", "Doors", "Back doors", "Windows", "Wall finishes", "Architecture", "Floors",
            "Kitchen", "Service", "Storage", "Sales", "Furniture", "Decor", "Lighting", "Logistics", BeltSection
        };

        public static IReadOnlyList<BuildCatalogFilter> Filters { get; } = (BuildCatalogFilter[])Enum.GetValues(typeof(BuildCatalogFilter));

        public static string FilterName(BuildCatalogFilter filter) => filter.ToString();

        public static string FilterElementName(BuildCatalogFilter filter) => $"build-filter-{filter.ToString().ToLowerInvariant()}";

        public static string ShellKey(BuildTool tool, string style) => $"shell-{tool.ToString().ToLowerInvariant()}-{style}";

        public static string CardName(string key) => $"build-item-{key}";

        public static bool Matches(BuildCatalogEntry entry, BuildCatalogFilter filter) => filter == BuildCatalogFilter.All || entry.Filter == filter;

        // The filter an equipment definition is listed under: by its authored palette category, else by what the piece does
        // (the register sells, a dock receives trucks, seats are furniture, refrigerated buffers store, any other machine cooks).
        public static BuildCatalogFilter FilterOf(EquipmentDefinition definition)
        {
            switch (definition.Category)
            {
                case "Architecture":
                case "Surfaces":
                    return BuildCatalogFilter.Structure;
                case "Furniture":
                    return BuildCatalogFilter.Furniture;
                case "Decor":
                case "Lighting":
                    return BuildCatalogFilter.Decor;
                case "Service":
                    return BuildCatalogFilter.Kitchen;
                case "Restaurant":
                    return BuildCatalogFilter.Sales;
            }
            if (definition.Kind == GoodsWorld.CounterKind) return BuildCatalogFilter.Sales;
            if (definition.Kind == GoodsWorld.DockKind) return BuildCatalogFilter.Logistics;
            if (definition.Seats > 0) return BuildCatalogFilter.Furniture;
            if (definition.InputRefrigerated || definition.OutputRefrigerated) return BuildCatalogFilter.Storage;
            return BuildCatalogFilter.Kitchen;
        }

        // The heading an equipment card is listed under: its palette category (Surfaces read as Floors), else its filter's name.
        public static string SectionOf(EquipmentDefinition definition) => definition.Category switch
        {
            "Surfaces" => "Floors",
            "Restaurant" => "Sales",
            "" => FilterName(FilterOf(definition)),
            _ => definition.Category
        };

        // Every card, in heading order: structure styles first, then equipment offers (trucks are bought elsewhere), then belt
        // packs. styleName gives a style's shown name; pricePercent is the site's district price percentage.
        public static List<BuildCatalogEntry> Entries(IEnumerable<OfferAsset> offers, IEnumerable<ItemDefinition> items,
            Func<string, string> styleName, int pricePercent)
        {
            var entries = new List<BuildCatalogEntry>();
            long Price(long cents) => GoodsWorld.ScaledCents(cents, pricePercent);
            void Shell(BuildTool tool, string section, IEnumerable<string> styles, Func<string, string> title, long cents, string suffix = "")
            {
                foreach (var style in styles)
                    entries.Add(new BuildCatalogEntry
                    {
                        Key = ShellKey(tool, style), Filter = BuildCatalogFilter.Structure, Section = section, Title = title(styleName(style)),
                        PriceCents = cents, PriceSuffix = suffix, Tool = tool, Style = style
                    });
            }
            Shell(BuildTool.Wall, "Walls", GoodsWorld.WallStyles, name => $"{name} wall", Price(GoodsWorld.ShellWallCellCents), " / m");
            Shell(BuildTool.Door, "Doors", GoodsWorld.DoorStyles, name => name, Price(GoodsWorld.ShellDoorCents));
            Shell(BuildTool.BackDoor, "Back doors", GoodsWorld.DoorStyles, name => $"{name} (back door)", Price(GoodsWorld.ShellDoorCents));
            Shell(BuildTool.Window, "Windows", GoodsWorld.WindowStyles, name => name, Price(GoodsWorld.ShellWindowCents));
            Shell(BuildTool.Finish, "Wall finishes", GoodsWorld.WallStyles, name => $"{name} finish", 0);
            var listed = (offers ?? Enumerable.Empty<OfferAsset>()).Where(x => x != null && x.Truck == null).ToList();
            foreach (var offer in listed.Where(x => x.Equipment != null))
                entries.Add(new BuildCatalogEntry
                {
                    Key = offer.Id, Filter = FilterOf(offer.Equipment), Section = SectionOf(offer.Equipment), Title = offer.Equipment.DisplayName,
                    PriceCents = offer.PriceCents, Icon = offer.Equipment.Icon, Tool = BuildTool.Item, OfferId = offer.Id
                });
            var belt = (items ?? Enumerable.Empty<ItemDefinition>()).FirstOrDefault(x => x != null && x.Id == GoodsWorld.BeltItemId);
            foreach (var offer in listed.Where(x => x.Equipment == null && x.ItemId == GoodsWorld.BeltItemId))
                entries.Add(new BuildCatalogEntry
                {
                    Key = offer.Id, Filter = BuildCatalogFilter.Logistics, Section = BeltSection, Title = $"{belt?.DisplayName ?? "Belt"} x{offer.Quantity}",
                    PriceCents = offer.PriceCents, Icon = belt?.Icon, Tool = BuildTool.None, OfferId = offer.Id
                });
            int Rank(string section)
            {
                var index = Array.IndexOf(SectionOrder, section);
                return index >= 0 ? index : SectionOrder.Length;
            }
            // OrderBy is stable, so cards keep their offer order inside a heading and unlisted headings their first appearance.
            return entries.OrderBy(x => Rank(x.Section)).ThenBy(x => Rank(x.Section) < SectionOrder.Length ? 0 : entries.FindIndex(e => e.Section == x.Section))
                .ToList();
        }
    }
}
