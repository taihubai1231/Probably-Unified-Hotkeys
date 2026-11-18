using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace ProbablyUnifiedHotkeys.Tests
{
    [Collection("SequentialStateTests")]
    public class ArchitecturePurityTests : TestBase
    {
        private static readonly Type[] CoreDomainTypes = new[]
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
            typeof(KeySearchMatcher)
        };

        private static readonly string[] ForbiddenKeywords = new[]
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

        [Fact]
        public void CoreDomainModels_ZeroUIAndHarmonyReferences()
        {
            var violations = new List<string>();

            foreach (var coreType in CoreDomainTypes)
            {
                CheckTypePurity(coreType, violations);

                // Also check any nested types
                foreach (var nested in coreType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                {
                    CheckTypePurity(nested, violations);
                }
            }

            Assert.True(
                violations.Count == 0,
                $"Architecture purity violations found in Core layer ({violations.Count} items):\n" +
                string.Join("\n", violations)
            );
        }

        private static void CheckTypePurity(Type type, List<string> violations)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            // 1. Check BaseType and Interfaces
            if (type.BaseType != null && type.BaseType != typeof(object) && type.BaseType != typeof(ValueType))
            {
                CheckTypeReference(type, "BaseType", type.BaseType, violations);
            }
            foreach (var iface in type.GetInterfaces())
            {
                CheckTypeReference(type, $"Interface({iface.Name})", iface, violations);
            }

            // 2. Check Fields
            foreach (var field in type.GetFields(flags))
            {
                CheckTypeReference(type, $"Field({field.Name})", field.FieldType, violations);
            }

            // 3. Check Properties
            foreach (var prop in type.GetProperties(flags))
            {
                CheckTypeReference(type, $"Property({prop.Name})", prop.PropertyType, violations);
            }

            // 4. Check Methods
            foreach (var method in type.GetMethods(flags))
            {
                // Skip compiler-generated property/event accessors to avoid duplicate reporting
                CheckTypeReference(type, $"Method({method.Name}) ReturnType", method.ReturnType, violations);
                foreach (var param in method.GetParameters())
                {
                    CheckTypeReference(type, $"Method({method.Name}) Param({param.Name})", param.ParameterType, violations);
                }
                if (method.IsGenericMethod)
                {
                    foreach (var arg in method.GetGenericArguments())
                    {
                        CheckTypeReference(type, $"Method({method.Name}) GenericArg({arg.Name})", arg, violations);
                    }
                }
            }

            // 5. Check Events
            foreach (var evt in type.GetEvents(flags))
            {
                if (evt.EventHandlerType != null)
                {
                    CheckTypeReference(type, $"Event({evt.Name})", evt.EventHandlerType, violations);
                }
            }
        }

        private static void CheckTypeReference(Type ownerType, string memberDescription, Type targetType, List<string> violations)
        {
            foreach (var leafType in EnumerateTypes(targetType))
            {
                string? fullName = leafType.FullName;
                string? ns = leafType.Namespace;
                string? asm = leafType.Assembly?.GetName()?.Name;

                foreach (var forbidden in ForbiddenKeywords)
                {
                    bool match = (fullName != null && fullName.IndexOf(forbidden, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                 (ns != null && ns.IndexOf(forbidden, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                 (asm != null && asm.IndexOf(forbidden, StringComparison.OrdinalIgnoreCase) >= 0);

                    // Note: UnityEngine.Color and KeyCode are in UnityEngine.CoreModule and are allowed
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
