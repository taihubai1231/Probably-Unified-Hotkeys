using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Xunit;

namespace ProbablyUnifiedHotkeys.Tests
{
    [Collection("SequentialStateTests")]
    public class Batch3ClientArchitectureAndStateTests : TestBase
    {
        private static readonly Type[] AllCoreDomainTypes = new[]
        {
            typeof(ModHotkeyEntry),
            typeof(ModGeneralEntry),
            typeof(ModOptionGroup),
            typeof(KeyCombo),
            typeof(KeyModifierMask),
            typeof(OptionKind),
            typeof(SearchScope),
            typeof(NormalDisplayMode),
            typeof(DiagnosticFilterMode),
            typeof(ModDiagnosticStatus),
            typeof(ConflictGroup),
            typeof(DiagnosticCandidateEntry),
            typeof(ModDiagnosticItem),
            typeof(ModScanReport),
            typeof(ConflictDetector),
            typeof(PinyinEngine),
            typeof(TradSimpTable),
            typeof(KeySearchMatcher),
            typeof(ProfileSnapshot),
            typeof(HotkeyProfileManager),
            typeof(ComboKeyStore),
            typeof(PrefsStorage),
            typeof(BilingualText),
            typeof(ModFeatureConfig),
            typeof(ModLogger),
            typeof(SafeExecution)
        };

        private static readonly Type[] AllAdapterDomainTypes = new[]
        {
            typeof(SectionLocalizer),
            typeof(ModCatalogCache),
            typeof(ModOptionScanner),
            typeof(KnownModAdapters),
            typeof(DllKeyProbe),
            typeof(ProbedKeyRecord)
        };

        private static readonly string[] CoreForbiddenKeywords = new[]
        {
            "UnityEngine.GameObject",
            "UnityEngine.UI.",
            "TMPro",
            "TextMeshPro",
            "HarmonyLib",
            "0Harmony",
            "CustomUIElement",
            "CustomUIBuilder",
            "VanillaOptionItem",
            "VanillaSectionCard",
            "GameOptionRegistry"
        };

        private static readonly string[] AdapterForbiddenKeywords = new[]
        {
            "TMPro",
            "TextMeshPro",
            "UnityEngine.UI."
        };

        [Fact]
        public void FinalAssembly_CoreLayer_ZeroUIAndHarmonyReferences()
        {
            var violations = new List<string>();

            foreach (var coreType in AllCoreDomainTypes)
            {
                CheckTypePurity(coreType, CoreForbiddenKeywords, violations);
                foreach (var nested in coreType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                {
                    CheckTypePurity(nested, CoreForbiddenKeywords, violations);
                }
            }

            Assert.True(
                violations.Count == 0,
                $"Architecture purity violations found in Core layer ({violations.Count} items):\n" +
                string.Join("\n", violations)
            );
        }

        [Fact]
        public void FinalAssembly_AdapterLayer_ZeroTMProAndUnityEngineUIPurity()
        {
            var violations = new List<string>();

            foreach (var adapterType in AllAdapterDomainTypes)
            {
                CheckTypePurity(adapterType, AdapterForbiddenKeywords, violations);
                foreach (var nested in adapterType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                {
                    CheckTypePurity(nested, AdapterForbiddenKeywords, violations);
                }
            }

            Assert.True(
                violations.Count == 0,
                $"Architecture purity violations found in Adapter layer ({violations.Count} items):\n" +
                string.Join("\n", violations)
            );
        }

        [Fact]
        public void FinalAssembly_ClientArchitecture_NineUiComponentsAndStateAreIndependent()
        {
            var uiComponentTypes = new[]
            {
                typeof(CollapsibleCardView),
                typeof(ProfilePresetCardView),
                typeof(ConflictAggregationView),
                typeof(KeyCaptureController),
                typeof(ModDiagnosticView),
                typeof(TopSearchAndActionBar),
                typeof(UiLayoutAndFontHelper),
                typeof(UnifiedOptionWindow),
                typeof(UnsavedConfirmDialog),
                typeof(UiSessionState),
                typeof(CardDragReorderController)
            };

            foreach (var type in uiComponentTypes)
            {
                Assert.NotNull(type);
                Assert.True(type.Assembly == typeof(UnifiedOptionWindow).Assembly, $"{type.Name} must belong to the main mod assembly.");
                Assert.False(type.IsNested, $"{type.Name} should be a standalone top-level class, not an embedded nested type.");
            }

            // Verify UnifiedOptionWindow.Instance implements IOptionUiBridge completely (all 18 interface members)
            Assert.NotNull(UnifiedOptionWindow.Instance);
            Assert.IsAssignableFrom<IOptionUiBridge>(UnifiedOptionWindow.Instance);
            IOptionUiBridge bridge = UnifiedOptionWindow.Instance;

            var interfaceMap = typeof(UnifiedOptionWindow).GetInterfaceMap(typeof(IOptionUiBridge));
            Assert.True(interfaceMap.InterfaceMethods.Length >= 18, "IOptionUiBridge methods and property accessors must all be mapped.");
            foreach (var targetMethod in interfaceMap.TargetMethods)
            {
                Assert.NotNull(targetMethod);
            }

            // Verify safe properties and lifecycle methods
            Assert.False(bridge.IsEnhancedWindowOpen);
            Assert.False(bridge.IsCapturingAnyKey);
            Assert.False(bridge.IsCapturingModHotkey);
            Assert.False(bridge.IsSuppressingEscapeAfterCapture);
            Assert.False(bridge.IsExplicitWindowClosing);
            Assert.False(bridge.IsInEnhancedRefreshRules);
            Assert.Null(bridge.CurrentWindow);
            Assert.Null(bridge.UnsavedConfirmWindow);
            Assert.False(bridge.HasUnsavedChanges());

            bridge.MarkEscapeConsumed();
            bridge.ResetSessionState();
        }

        [Fact]
        public void UnifiedOptionWindow_EnsureBridgeRegistered_MountsOptionUiHostActive()
        {
            OptionUiHost.Active = null;
            Assert.Null(OptionUiHost.Active);

            UnifiedOptionWindow.EnsureBridgeRegistered();

            Assert.NotNull(OptionUiHost.Active);
            Assert.Same(UnifiedOptionWindow.Instance, OptionUiHost.Active);

            Assert.NotNull(UiSessionState.ApplyAllSettingsCallback);
            Assert.NotNull(UiSessionState.RefreshConflictsCallback);
            Assert.NotNull(UiSessionState.RebuildConflictViewCallback);
            Assert.NotNull(UiSessionState.OnFeatureConfigChangedCallback);
        }

        [Fact]
        public void UiSessionState_ModelHandleDictionaries_ClearOnCacheInvalidateAndReset()
        {
            var group = new ModOptionGroup { ModId = "test.group.purity" };
            var hotkey = new ModHotkeyEntry { Id = "test.hk.purity", ModId = "test.mod" };
            var general = new ModGeneralEntry { Id = "test.gen.purity", ModId = "test.mod" };

            var parts = UiSessionState.GetOrCreateCardParts(group);
            var hkUi = UiSessionState.GetOrCreateHotkeyUi(hotkey);
            var genUi = UiSessionState.GetOrCreateGeneralUi(general);

            Assert.NotNull(parts);
            Assert.NotNull(hkUi);
            Assert.NotNull(genUi);

            Assert.True(UiSessionState.TryGetCardParts(group, out _));
            Assert.True(UiSessionState.TryGetHotkeyUi(hotkey, out _));
            Assert.True(UiSessionState.TryGetGeneralUi(general, out _));

            ModCatalogCache.Invalidate();

            Assert.False(UiSessionState.TryGetCardParts(group, out _), "CardParts should be cleared on Invalidate");
            Assert.False(UiSessionState.TryGetHotkeyUi(hotkey, out _), "HotkeyUiMap should be cleared on Invalidate");
            Assert.False(UiSessionState.TryGetGeneralUi(general, out _), "GeneralUiMap should be cleared on Invalidate");

            UiSessionState.GetOrCreateCardParts(group);
            UiSessionState.GetOrCreateHotkeyUi(hotkey);
            UiSessionState.GetOrCreateGeneralUi(general);

            Assert.True(UiSessionState.TryGetCardParts(group, out _));
            Assert.True(UiSessionState.TryGetHotkeyUi(hotkey, out _));
            Assert.True(UiSessionState.TryGetGeneralUi(general, out _));

            ModCatalogCache.ResetPendingSessionState(new List<ModOptionGroup>(), new List<ModHotkeyEntry>());

            Assert.False(UiSessionState.TryGetCardParts(group, out _), "CardParts should be cleared on ResetPendingSessionState");
            Assert.False(UiSessionState.TryGetHotkeyUi(hotkey, out _), "HotkeyUiMap should be cleared on ResetPendingSessionState");
            Assert.False(UiSessionState.TryGetGeneralUi(general, out _), "GeneralUiMap should be cleared on ResetPendingSessionState");
        }

        [Fact]
        public void UiSessionState_HasUnsavedChanges_KeyCodeNone_Unbind_ReturnsFalse_LockingV147Contract()
        {
            try
            {
                UiSessionState.AllHotkeys = new List<ModHotkeyEntry>();
                Assert.False(UiSessionState.HasUnsavedChanges(), "Clean session must not report unsaved changes");

                var unboundModKey = new ModHotkeyEntry
                {
                    Id = "test.mod.unbound",
                    ModId = "test.mod",
                    DisplayName = "Unbound Key",
                    GetKey = () => KeyCode.None,
                    HasPendingChange = false
                };
                UiSessionState.AllHotkeys = new List<ModHotkeyEntry> { unboundModKey };
                Assert.False(UiSessionState.HasUnsavedChanges(), "Unbound hotkey with no pending changes must not report unsaved changes");

                var explicitNoneModKey = new ModHotkeyEntry
                {
                    Id = "test.mod.explicit_none",
                    ModId = "test.mod",
                    DisplayName = "Explicit None",
                    GetKey = () => KeyCode.None,
                    PendingKey = KeyCode.None,
                    HasPendingChange = true
                };
                UiSessionState.AllHotkeys = new List<ModHotkeyEntry> { explicitNoneModKey };
                Assert.False(UiSessionState.HasUnsavedChanges(), "Mod hotkey with KeyCode.None must be exempted from fake dirty flag");

                var vanillaKey = new ModHotkeyEntry
                {
                    Id = "vanilla.unbound",
                    ModId = "vanilla",
                    DisplayName = "Vanilla Unbound",
                    GetKey = () => KeyCode.None,
                    PendingKey = KeyCode.None,
                    HasPendingChange = true
                };
                UiSessionState.AllHotkeys = new List<ModHotkeyEntry> { vanillaKey };
                Assert.False(UiSessionState.HasUnsavedChanges(), "Vanilla hotkey with KeyCode.None must not report fake dirty changes");

                var dirtyKey = new ModHotkeyEntry
                {
                    Id = "test.mod.dirty",
                    ModId = "test.mod",
                    DisplayName = "Dirty Key",
                    GetKey = () => KeyCode.F1,
                    PendingKey = KeyCode.F2,
                    HasPendingChange = true
                };
                UiSessionState.AllHotkeys = new List<ModHotkeyEntry> { dirtyKey };
                Assert.True(UiSessionState.HasUnsavedChanges(), "Actual pending key difference must report unsaved changes");
            }
            finally
            {
                UiSessionState.AllHotkeys = new List<ModHotkeyEntry>();
            }
        }

        private static void CheckTypePurity(Type type, string[] forbiddenKeywords, List<string> violations)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            if (type.BaseType != null && type.BaseType != typeof(object) && type.BaseType != typeof(ValueType))
            {
                CheckTypeReference(type, "BaseType", type.BaseType, forbiddenKeywords, violations);
            }
            foreach (var iface in type.GetInterfaces())
            {
                CheckTypeReference(type, $"Interface({iface.Name})", iface, forbiddenKeywords, violations);
            }

            foreach (var field in type.GetFields(flags))
            {
                CheckTypeReference(type, $"Field({field.Name})", field.FieldType, forbiddenKeywords, violations);
            }

            foreach (var prop in type.GetProperties(flags))
            {
                CheckTypeReference(type, $"Property({prop.Name})", prop.PropertyType, forbiddenKeywords, violations);
            }

            foreach (var method in type.GetMethods(flags))
            {
                CheckTypeReference(type, $"Method({method.Name}) ReturnType", method.ReturnType, forbiddenKeywords, violations);
                foreach (var param in method.GetParameters())
                {
                    CheckTypeReference(type, $"Method({method.Name}) Param({param.Name})", param.ParameterType, forbiddenKeywords, violations);
                }
                if (method.IsGenericMethod)
                {
                    foreach (var arg in method.GetGenericArguments())
                    {
                        CheckTypeReference(type, $"Method({method.Name}) GenericArg({arg.Name})", arg, forbiddenKeywords, violations);
                    }
                }
            }

            foreach (var evt in type.GetEvents(flags))
            {
                if (evt.EventHandlerType != null)
                {
                    CheckTypeReference(type, $"Event({evt.Name})", evt.EventHandlerType, forbiddenKeywords, violations);
                }
            }
        }

        private static void CheckTypeReference(Type ownerType, string memberDescription, Type targetType, string[] forbiddenKeywords, List<string> violations)
        {
            foreach (var leafType in EnumerateTypes(targetType))
            {
                string? fullName = leafType.FullName;
                string? ns = leafType.Namespace;
                string? asm = leafType.Assembly?.GetName()?.Name;

                foreach (var forbidden in forbiddenKeywords)
                {
                    bool match = (fullName != null && fullName.IndexOf(forbidden, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                 (ns != null && ns.IndexOf(forbidden, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                 (asm != null && asm.IndexOf(forbidden, StringComparison.OrdinalIgnoreCase) >= 0);

                    if (match && leafType.FullName == "UnityEngine.Color") continue;
                    if (match && leafType.FullName == "UnityEngine.KeyCode") continue;

                    if (match)
                    {
                        violations.Add($"[{ownerType.Name}] {memberDescription} references forbidden type: {leafType.AssemblyQualifiedName}");
                    }
                }
            }
        }

        private static IEnumerable<Type> EnumerateTypes(Type type)
        {
            if (type == null) yield break;

            if (type.IsByRef || type.IsPointer || type.IsArray)
            {
                var elem = type.GetElementType();
                if (elem != null)
                {
                    foreach (var t in EnumerateTypes(elem))
                    {
                        yield return t;
                    }
                }
                yield break;
            }

            yield return type;

            if (type.IsGenericType)
            {
                foreach (var arg in type.GetGenericArguments())
                {
                    foreach (var t in EnumerateTypes(arg))
                    {
                        yield return t;
                    }
                }
            }
        }
    }
}
