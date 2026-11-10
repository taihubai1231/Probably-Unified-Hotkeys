using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AllowUnbindKey
{
    /// <summary>
    /// 针对使用非标准配置存储的知名创意工坊模组的高速专属适配器
    /// </summary>
    public static class KnownModAdapters
    {
        public static List<ModOptionGroup> TryAdaptKnownMods()
        {
            var groups = new List<ModOptionGroup>();

            TryAdaptShowItemNames(groups);
            TryAdaptConflictScanner(groups);
            TryAdaptFactionReputation(groups);
            TryAdaptClientDemandHighlight(groups);
            TryAdaptPenQoL(groups);
            TryAdaptSmartInventoryArrange(groups);

            return groups;
        }

        // 1. 物品名称常显 (ShowItemNames)
        private static void TryAdaptShowItemNames(List<ModOptionGroup> groups)
        {
            Type? settingsType = AccessTools.TypeByName("ShowItemNames.Settings");
            if (settingsType == null) return;

            var group = new ModOptionGroup
            {
                ModId = "ShowItemNames",
                DisplayName = "物品名称常显",
                PinyinSortKey = PinyinEngine.ToInitials("物品名称常显")
            };

            PropertyInfo? hotkeyProp = AccessTools.Property(settingsType, "Hotkey");
            MethodInfo? setHotkey = AccessTools.Method(settingsType, "SetHotkey", new Type[] { typeof(KeyCode) });
            MethodInfo? commit = AccessTools.Method(settingsType, "Commit");

            if (hotkeyProp != null)
            {
                group.Hotkeys.Add(new ModHotkeyEntry
                {
                    Id = "ShowItemNames.Hotkey",
                    ModId = group.ModId,
                    ModDisplayName = group.DisplayName,
                    DisplayName = "常显开关热键",
                    Tooltip = "按压或按下该快捷键以切换物品名称常显",
                    DefaultKey = KeyCode.LeftAlt,
                    GetKey = () => (KeyCode)(hotkeyProp.GetValue(null) ?? KeyCode.None),
                    SetKey = (key) =>
                    {
                        if (setHotkey != null)
                        {
                            setHotkey.Invoke(null, new object[] { key });
                        }
                        else
                        {
                            hotkeyProp.SetValue(null, key);
                            PlayerPrefs.SetInt("snd.hotkey", (int)key);
                            commit?.Invoke(null, null);
                        }
                    }
                });
            }

            if (group.TotalItemCount > 0) groups.Add(group);
        }

        // 2. 模组冲突检测器 (ConflictScanner)
        private static void TryAdaptConflictScanner(List<ModOptionGroup> groups)
        {
            Type? keybindType = AccessTools.TypeByName("ConflictScanner.Keybind");
            if (keybindType == null) return;

            var group = new ModOptionGroup
            {
                ModId = "conflict_scanner",
                DisplayName = "模组冲突检测器",
                PinyinSortKey = PinyinEngine.ToInitials("模组冲突检测器")
            };

            PropertyInfo? keyProp = AccessTools.Property(keybindType, "Key");
            MethodInfo? saveMethod = AccessTools.Method(keybindType, "Save", new Type[] { typeof(KeyCode) });

            if (keyProp != null)
            {
                group.Hotkeys.Add(new ModHotkeyEntry
                {
                    Id = "conflict_scanner.ReportKey",
                    ModId = group.ModId,
                    ModDisplayName = group.DisplayName,
                    DisplayName = "冲突报告窗口快捷键",
                    Tooltip = "按下后呼出模组冲突检测器诊断面板",
                    DefaultKey = KeyCode.F8,
                    GetKey = () => (KeyCode)(keyProp.GetValue(null) ?? KeyCode.None),
                    SetKey = (key) =>
                    {
                        if (saveMethod != null)
                        {
                            saveMethod.Invoke(null, new object[] { key });
                        }
                        else
                        {
                            keyProp.SetValue(null, key);
                            PlayerPrefs.SetString("conflict_scanner.report_key", key.ToString());
                            PlayerPrefs.Save();
                        }
                    }
                });
            }

            if (group.TotalItemCount > 0) groups.Add(group);
        }

        // 3. 势力声望拓展 (FactionReputationExpansion)
        private static void TryAdaptFactionReputation(List<ModOptionGroup> groups)
        {
            Type? repModType = AccessTools.TypeByName("FactionReputationExpansion.RepMod");
            if (repModType == null) return;

            var group = new ModOptionGroup
            {
                ModId = "faction_reputation_expansion",
                DisplayName = "势力声望扩展",
                PinyinSortKey = PinyinEngine.ToInitials("势力声望扩展")
            };

            PropertyInfo? journalProp = AccessTools.Property(repModType, "JournalKey");
            MethodInfo? setJournalKey = AccessTools.Method(repModType, "SetJournalKey", new Type[] { typeof(KeyCode), typeof(bool) });

            if (journalProp != null)
            {
                group.Hotkeys.Add(new ModHotkeyEntry
                {
                    Id = "faction_reputation.journal_key",
                    ModId = group.ModId,
                    ModDisplayName = group.DisplayName,
                    DisplayName = "打开势力声望手帐",
                    Tooltip = "按此键在游戏中打开或关闭各势力声望详情界面",
                    DefaultKey = KeyCode.J,
                    GetKey = () => (KeyCode)(journalProp.GetValue(null) ?? KeyCode.None),
                    SetKey = (key) =>
                    {
                        if (setJournalKey != null)
                        {
                            setJournalKey.Invoke(null, new object[] { key, false });
                        }
                        else
                        {
                            journalProp.SetValue(null, key);
                        }
                    }
                });
            }

            if (group.TotalItemCount > 0) groups.Add(group);
        }


        // 5. 客户需求高亮 (ClientDemandHighlight)
        private static void TryAdaptClientDemandHighlight(List<ModOptionGroup> groups)
        {
            Type? cfgType = AccessTools.TypeByName("ClientDemandHighlight.Config");
            if (cfgType == null) return;

            PropertyInfo? toggleProp = AccessTools.Property(cfgType, "ToggleKey");
            FieldInfo? toggleField = AccessTools.Field(cfgType, "_toggleKey");
            if (toggleProp == null && toggleField == null) return;

            var group = new ModOptionGroup
            {
                ModId = "hollow.clientdemandhighlight",
                DisplayName = "客户需求高亮",
                PinyinSortKey = PinyinEngine.ToInitials("客户需求高亮")
            };

            group.Hotkeys.Add(new ModHotkeyEntry
            {
                Id = "clientdemandhighlight.toggle_key",
                ModId = group.ModId,
                ModDisplayName = group.DisplayName,
                DisplayName = "切换高亮热键",
                Tooltip = "按此键切换是否高亮当前顾客需求的特殊物品",
                DefaultKey = KeyCode.H,
                GetKey = () => (KeyCode)((toggleProp?.GetValue(null) ?? toggleField?.GetValue(null)) ?? KeyCode.None),
                SetKey = (key) =>
                {
                    if (toggleProp != null && toggleProp.CanWrite) toggleProp.SetValue(null, key);
                    else toggleField?.SetValue(null, key);
                    PlayerPrefs.SetInt("clientdemandhighlight.key", (int)key);
                    PlayerPrefs.Save();
                }
            });

            if (group.TotalItemCount > 0) groups.Add(group);
        }

        // 6. Pen 便利性优化 (Pen_mod_QoL_Tweaks)
        private static void TryAdaptPenQoL(List<ModOptionGroup> groups)
        {
            Type? qolKeys = AccessTools.TypeByName("PenLabelerAssist.QoLKeyBindings");
            if (qolKeys == null) return;

            var group = new ModOptionGroup
            {
                ModId = "Pen_mod_QoL_Tweaks",
                DisplayName = "Pen的便利性优化",
                PinyinSortKey = PinyinEngine.ToInitials("Pen的便利性优化")
            };

            string[] propNames = new string[] { "QuickBuy", "QuickSell", "QuickAppraisal", "QuickConfront", "QuickPin", "TabLeft", "TabRight" };
            string[] labels = new string[] { "一键快速购买", "一键快速出售", "一键快速估价", "一键当面对质", "一键快捷锁定", "左切换页签", "右切换页签" };

            for (int i = 0; i < propNames.Length; i++)
            {
                string propName = propNames[i];
                string label = labels[i];
                PropertyInfo? prop = AccessTools.Property(qolKeys, propName);
                if (prop != null)
                {
                    group.Hotkeys.Add(new ModHotkeyEntry
                    {
                        Id = $"pen_qol.{propName.ToLowerInvariant()}",
                        ModId = group.ModId,
                        ModDisplayName = group.DisplayName,
                        DisplayName = label,
                        Tooltip = $"Pen便利性优化操作快捷键：{label}",
                        DefaultKey = (KeyCode)(prop.GetValue(null) ?? KeyCode.None),
                        GetKey = () => (KeyCode)(prop.GetValue(null) ?? KeyCode.None),
                        SetKey = (key) =>
                        {
                            if (prop.CanWrite) prop.SetValue(null, key);
                        }
                    });
                }
            }

            if (group.TotalItemCount > 0) groups.Add(group);
        }

        // 7. 智能物品整理 (SmartInventoryArrange)
        private static void TryAdaptSmartInventoryArrange(List<ModOptionGroup> groups)
        {
            Type? runtimeType = AccessTools.TypeByName("SmartInventoryArrange.SettingsRuntime");
            if (runtimeType == null) return;

            var group = new ModOptionGroup
            {
                ModId = "smart_inventory_arrange",
                DisplayName = "智能物品整理",
                PinyinSortKey = PinyinEngine.ToInitials("智能物品整理")
            };

            PropertyInfo? enabledProp = AccessTools.Property(runtimeType, "ArrangeHotkeyEnabled");
            if (enabledProp != null)
            {
                group.GeneralSettings.Add(new ModGeneralEntry
                {
                    Id = "smart_inventory_arrange.enabled",
                    ModId = group.ModId,
                    DisplayName = "启用快捷键一键整理",
                    Tooltip = "关闭后快捷键整理功能将暂停响应",
                    Kind = OptionKind.Toggle,
                    GetBool = () => (bool)(enabledProp.GetValue(null) ?? true),
                    SetBool = (val) => { if (enabledProp.CanWrite) enabledProp.SetValue(null, val); }
                });
            }

            if (group.TotalItemCount > 0) groups.Add(group);
        }
    }
}
