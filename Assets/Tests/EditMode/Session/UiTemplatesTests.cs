// Checks the UI's authoring (decision 0042): every UXML under Assets/UI/Resources/UI loads through UiTemplates and clones,
// item templates have the single root Clone needs, and every screen stylesheet is imported by the panels' theme.
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session.Tests
{
    public sealed class UiTemplatesTests
    {
        private const string Root = "Assets/UI/Resources/UI/";
        private const string Theme = "Assets/UI/DevRuntimeTheme.tss";

        private static string[] Templates() => AssetDatabase.FindAssets("t:VisualTreeAsset", new[] { "Assets/UI/Resources/UI" })
            .Select(AssetDatabase.GUIDToAssetPath).OrderBy(x => x).ToArray();

        [Test]
        public void EveryTemplateLoadsAndClones()
        {
            var paths = Templates();
            Assert.That(paths.Length, Is.GreaterThanOrEqualTo(30), "The UI templates are found.");
            foreach (var path in paths)
            {
                var name = path.Substring(Root.Length, path.Length - Root.Length - ".uxml".Length);
                var holder = new VisualElement();
                Assert.DoesNotThrow(() => UiTemplates.Asset(name).CloneTree(holder), name);
                Assert.That(holder.childCount, Is.GreaterThan(0), name);
            }
        }

        [Test]
        public void ItemTemplatesHaveOneRootAndScreensAnFfRoot()
        {
            string[] screens = { "MainMenu/MainMenu", "Hud/Hud", "Employee/EmployeeScript", "Build/Build", "Logistics/Logistics", "Property/Property" };
            foreach (var screen in screens)
            {
                var holder = new VisualElement();
                UiTemplates.Fill(holder, screen);
                Assert.That(holder.Q(className: "ff-root"), Is.Not.Null, $"{screen} has an ff-root element for text colour.");
            }
            string[] items =
            {
                "MainMenu/MenuMain", "MainMenu/MenuWorlds", "MainMenu/MenuWorldRow", "MainMenu/MenuConnect", "MainMenu/MenuSettings",
                "MainMenu/MenuKeybindGroup", "MainMenu/MenuKeybindRow", "Hud/HudSlot", "Hud/HudWindow", "Hud/HudOfferRow",
                "Hud/HudStaffWindow", "Hud/HudEmployeeRow", "Hud/HudConstructionWindow", "Hud/HudMachineWindow", "Hud/HudDockWindow",
                "Hud/HudTableWindow", "Hud/HudStorageWindow", "Employee/EmployeeTaskCard", "Employee/EmployeePlace",
                "Employee/EmployeeChip", "Employee/EmployeeSuggestion", "Employee/EmployeeHand", "Build/BuildCatalogItem",
                "Logistics/LogisticsRouteCard", "Logistics/LogisticsTruckCard", "Logistics/LogisticsChooser",
                "Logistics/LogisticsSiteCard", "Logistics/LogisticsStockRow"
            };
            foreach (var item in items) Assert.DoesNotThrow(() => UiTemplates.Clone(item), item);
        }

        [Test]
        public void EveryScreenStylesheetIsImportedByTheTheme()
        {
            var theme = File.ReadAllText(Theme);
            Assert.That(theme, Does.Contain("Theme/Theme.uss"));
            var sheets = AssetDatabase.FindAssets("t:StyleSheet", new[] { "Assets/UI/Resources/UI" }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(x => x.EndsWith(".uss")).Distinct().ToList();
            Assert.That(sheets.Count, Is.EqualTo(6), "One sheet per screen.");
            foreach (var sheet in sheets)
                Assert.That(theme, Does.Contain(sheet.Substring("Assets/UI/".Length)), sheet);
        }
    }
}
