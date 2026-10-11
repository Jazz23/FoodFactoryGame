// Drives the Settings screen's interactive rebinding (decision 0041) with a virtual keyboard on a private copy of the project's
// action asset, so neither the project asset nor any real device is touched.
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FoodFactoryGame.Session.PlayModeTests
{
    public sealed class KeyRebindingTests
    {
        private readonly InputTestFixture _input = new InputTestFixture();
        private InputActionAsset _asset;
        private Keyboard _keyboard;

        [SetUp]
        public void SetUp()
        {
            _input.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            _asset = InputActionAsset.FromJson(File.ReadAllText("Assets/InputSystem_Actions.inputactions"));
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (_asset != null) Object.DestroyImmediate(_asset);
            }
            finally
            {
                _input.TearDown();
            }
        }

        private KeyBindingRow Row(string label) => KeyBindings.Groups(_asset).SelectMany(x => x.Rows).Single(x => x.Label == label);

        // Presses a key during a listen and lets the rebinding's short wait for a better match pass.
        private void PressDuringListen(UnityEngine.InputSystem.Controls.KeyControl key)
        {
            _input.Press(key);
            _input.currentTime += 0.5;
            InputSystem.Update();
            _input.Release(key);
            InputSystem.Update();
        }

        [Test]
        public void PressingAKeyRebindsThatSlotAndLeavesTheOtherAlone()
        {
            var jump = Row("Jump");
            jump.Action.Enable();
            bool? changed = null;
            KeyBindings.Rebind(jump.Action, jump.Alternate, x => changed = x);
            Assert.That(jump.Action.enabled, Is.False, "The action is off while listening.");

            PressDuringListen(_keyboard.gKey);

            Assert.That(changed, Is.True);
            Assert.That(jump.Action.bindings[jump.Alternate].effectivePath, Is.EqualTo("<Keyboard>/g"));
            Assert.That(jump.Action.bindings[jump.Primary].effectivePath, Is.EqualTo("<Keyboard>/space"));
            Assert.That(jump.Action.enabled, Is.True, "The action is back on afterwards.");
            // Overrides survive the JSON round trip the settings store uses.
            var json = _asset.SaveBindingOverridesAsJson();
            _asset.RemoveAllBindingOverrides();
            Assert.That(jump.Action.bindings[jump.Alternate].effectivePath, Is.Empty);
            _asset.LoadBindingOverridesFromJson(json);
            Assert.That(jump.Action.bindings[jump.Alternate].effectivePath, Is.EqualTo("<Keyboard>/g"));
        }

        [Test]
        public void BackspaceUnbindsAndEscapeCancels()
        {
            var build = Row("Build mode");
            bool? changed = null;
            KeyBindings.Rebind(build.Action, build.Primary, x => changed = x);
            PressDuringListen(_keyboard.backspaceKey);
            Assert.That(changed, Is.True);
            Assert.That(build.Action.bindings[build.Primary].effectivePath, Is.Empty);

            var rotate = Row("Rotate");
            changed = null;
            KeyBindings.Rebind(rotate.Action, rotate.Primary, x => changed = x);
            PressDuringListen(_keyboard.escapeKey);
            Assert.That(changed, Is.False);
            Assert.That(rotate.Action.bindings[rotate.Primary].effectivePath, Is.EqualTo("<Keyboard>/r"));
        }

        [Test]
        public void ARebindOntoAnotherActionsKeyIsReportedAsAConflict()
        {
            var rotate = Row("Rotate");
            KeyBindings.Rebind(rotate.Action, rotate.Primary, null);
            PressDuringListen(_keyboard.bKey);
            Assert.That(KeyBindings.ConflictsOf(_asset, rotate.Action, rotate.Primary), Is.EqualTo(new[] { "Build mode" }));
        }
    }
}
