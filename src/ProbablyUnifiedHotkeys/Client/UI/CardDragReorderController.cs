#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProbablyUnifiedHotkeys
{
    /// <summary>
    /// 模组卡片拖拽自由排序控制器：
    /// 实现 UGUI 拖拽接口，提供透传滚轮、EventSystem 阈值防误折、
    /// 暖琥珀金高亮指示线（ignoreLayout=true 零抖动零 TMP 重算）、
    /// 视口边缘自动平滑滚屏与松手当帧强制重排刷新。
    /// 严格遵循纯内存操作，落盘统一由 ApplyAllSettings 处理。
    /// </summary>
    public sealed class CardDragReorderController : MonoBehaviour,
        IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerDownHandler, IScrollHandler
    {
        public static int LastDragCompletedFrame = -10;

        public ModOptionGroup Group { get; set; } = null!;
        public GameObject CardRoot { get; set; } = null!;
        public RectTransform ContainerRt { get; set; } = null!;
        public ScrollRect? ScrollRect { get; set; }

        private bool _isDragActive;
        private int _initialGroupIndex = -1;
        private CanvasGroup? _cardCanvasGroup;

        private static GameObject? s_indicatorLineObj;
        private static RectTransform? s_indicatorLineRt;

        private void Awake()
        {
            _cardCanvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
        }

        private void OnDisable()
        {
            if (_cardCanvasGroup != null)
            {
                _cardCanvasGroup.alpha = 1f;
            }
            if (s_indicatorLineObj != null)
            {
                s_indicatorLineObj.SetActive(false);
            }
            _isDragActive = false;
        }

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            eventData.useDragThreshold = true;
        }

        public void OnScroll(PointerEventData eventData)
        {
            // 显式将滚轮滚动事件转发给父级 ScrollRect，绝不拦截吞滚轮
            if (ScrollRect != null)
            {
                ScrollRect.OnScroll(eventData);
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (!ModFeatureConfig.AllowCardDragReorder) return;
            if (IsDragRestrictedByFilter()) return;

            _isDragActive = false;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (!ModFeatureConfig.AllowCardDragReorder) return;
            if (IsDragRestrictedByFilter()) return;

            // 若开启了置顶自身模组，本模组卡片严禁被拖动
            if (ModFeatureConfig.PinOwnCardToTop && string.Equals(Group.ModId, ModFeatureConfig.ModId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _isDragActive = true;
            _initialGroupIndex = UiSessionState.ModGroups.IndexOf(Group);

            if (_cardCanvasGroup != null)
            {
                _cardCanvasGroup.alpha = 0.55f;
            }

            EnsureIndicatorLine(ContainerRt);
            if (s_indicatorLineObj != null)
            {
                s_indicatorLineObj.SetActive(true);
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_isDragActive || CardRoot == null || ContainerRt == null) return;

            // 将屏幕指针坐标精确转换到 ContainerRt 局部空间
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(ContainerRt, eventData.position, eventData.pressEventCamera, out Vector2 pointerLocal))
            {
                return;
            }

            // 动态计算目标逻辑插入位置与指示线世界坐标 Y
            int targetGroupIndex = CalculateTargetGroupIndex(pointerLocal.y, out float lineWorldY);

            // 定位指示线（暖琥珀金横线，左右留白 6px，高度 3px，严格免疫父容器 Pivot 偏移）
            if (s_indicatorLineRt != null && s_indicatorLineObj != null && s_indicatorLineObj.activeSelf)
            {
                s_indicatorLineRt.anchorMin = new Vector2(0f, 0.5f);
                s_indicatorLineRt.anchorMax = new Vector2(1f, 0.5f);
                s_indicatorLineRt.pivot = new Vector2(0.5f, 0.5f);
                s_indicatorLineRt.anchoredPosition = Vector2.zero;
                s_indicatorLineRt.sizeDelta = new Vector2(-12f, 3f);
                Vector3 worldPos = s_indicatorLineRt.position;
                worldPos.y = lineWorldY;
                s_indicatorLineRt.position = worldPos;
                s_indicatorLineRt.SetAsLastSibling();
            }

            // 视口上下边缘 15% 自动平滑滚动
            HandleEdgeAutoScroll(eventData.position);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_isDragActive) return;
            _isDragActive = false;

            if (_cardCanvasGroup != null)
            {
                _cardCanvasGroup.alpha = 1f;
            }

            if (ContainerRt == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(ContainerRt, eventData.position, eventData.pressEventCamera, out Vector2 pointerLocal);
            int targetGroupIndex = CalculateTargetGroupIndex(pointerLocal.y, out _);

            if (s_indicatorLineObj != null)
            {
                s_indicatorLineObj.SetActive(false);
                s_indicatorLineObj.transform.SetAsLastSibling();
            }

            // 若位置原位未动，直接返回 No-op
            if (_initialGroupIndex < 0 || targetGroupIndex == _initialGroupIndex || targetGroupIndex == _initialGroupIndex + 1)
            {
                return;
            }

            LastDragCompletedFrame = Time.frameCount;
            ApplyCardReorder(Group, _initialGroupIndex, targetGroupIndex, ContainerRt);
        }

        private static bool IsDragRestrictedByFilter()
        {
            // 搜索状态或非全部筛选状态下临时锁定拖拽，防止产生脏顺序
            return !string.IsNullOrEmpty(UiSessionState.SearchQuery) ||
                   UiSessionState.IsConflictViewActive ||
                   UiSessionState.IsDiagnosticViewActive ||
                   UiSessionState.NormalDisplayMode != NormalDisplayMode.All;
        }

        private int CalculateTargetGroupIndex(float pointerLocalY, out float lineWorldY)
        {
            lineWorldY = 0f;
            if (UiSessionState.ModGroups == null || UiSessionState.ModGroups.Count == 0 || ContainerRt == null)
            {
                return _initialGroupIndex >= 0 ? _initialGroupIndex : 0;
            }

            int bestGroupIndex = UiSessionState.ModGroups.Count;
            float fallbackLastWorldY = 0f;

            for (int i = 0; i < UiSessionState.ModGroups.Count; i++)
            {
                var g = UiSessionState.ModGroups[i];
                if (!UiSessionState.TryGetCardParts(g, out var parts) || parts.CardRootObject == null) continue;

                var cardRt = parts.CardRootObject.GetComponent<RectTransform>() ?? (parts.CardRootObject.transform as RectTransform);
                if (cardRt == null) continue;

                Vector3 cardCenterWorld = cardRt.TransformPoint(cardRt.rect.center);
                Vector2 cardCenterLocal = ContainerRt.InverseTransformPoint(cardCenterWorld);

                Vector3 cardTopWorld = cardRt.TransformPoint(new Vector2(0f, cardRt.rect.yMax));
                Vector3 cardBotWorld = cardRt.TransformPoint(new Vector2(0f, cardRt.rect.yMin));
                fallbackLastWorldY = cardBotWorld.y - 0.01f;

                if (pointerLocalY > cardCenterLocal.y)
                {
                    bestGroupIndex = i;
                    lineWorldY = cardTopWorld.y + 0.01f;
                    break;
                }
            }

            if (bestGroupIndex >= UiSessionState.ModGroups.Count)
            {
                lineWorldY = fallbackLastWorldY;
            }

            if (ModFeatureConfig.PinOwnCardToTop)
            {
                bestGroupIndex = Mathf.Clamp(bestGroupIndex, 1, UiSessionState.ModGroups.Count);
            }

            return bestGroupIndex;
        }

        private void HandleEdgeAutoScroll(Vector2 screenPos)
        {
            if (ScrollRect == null || ScrollRect.viewport == null) return;

            RectTransform viewport = ScrollRect.viewport;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, screenPos, null, out Vector2 localPos))
            {
                Rect rect = viewport.rect;
                float edgeThreshold = rect.height * 0.15f;

                if (localPos.y > rect.yMax - edgeThreshold)
                {
                    float factor = (localPos.y - (rect.yMax - edgeThreshold)) / edgeThreshold;
                    ScrollRect.verticalNormalizedPosition = Mathf.Clamp01(ScrollRect.verticalNormalizedPosition + factor * Time.deltaTime * 2.5f);
                }
                else if (localPos.y < rect.yMin + edgeThreshold)
                {
                    float factor = ((rect.yMin + edgeThreshold) - localPos.y) / edgeThreshold;
                    ScrollRect.verticalNormalizedPosition = Mathf.Clamp01(ScrollRect.verticalNormalizedPosition - factor * Time.deltaTime * 2.5f);
                }
            }
        }

        private static void EnsureIndicatorLine(RectTransform? parent)
        {
            if (parent == null) return;
            if (s_indicatorLineObj != null && s_indicatorLineRt != null)
            {
                if (s_indicatorLineRt.parent != parent)
                {
                    s_indicatorLineRt.SetParent(parent, false);
                }
                return;
            }

            s_indicatorLineObj = new GameObject("Puh_CardInsertionIndicator");
            s_indicatorLineRt = s_indicatorLineObj.AddComponent<RectTransform>();
            s_indicatorLineRt.SetParent(parent, false);

            var img = s_indicatorLineObj.AddComponent<Image>();
            img.color = new Color(0.90f, 0.72f, 0.36f, 0.92f); // 暖琥珀金高亮
            img.raycastTarget = false;

            var le = s_indicatorLineObj.AddComponent<LayoutElement>();
            le.ignoreLayout = true; // 绝对不参与 VerticalLayoutGroup 排版，彻底根除 5px 来回抖动

            s_indicatorLineObj.SetActive(false);
        }

        private static void ApplyCardReorder(ModOptionGroup group, int oldGroupIndex, int targetGroupIndex, RectTransform? containerRt)
        {
            if (group == null || oldGroupIndex < 0 || oldGroupIndex >= UiSessionState.ModGroups.Count) return;

            // 1. 逻辑列表换位
            UiSessionState.ModGroups.RemoveAt(oldGroupIndex);
            int insertIndex = targetGroupIndex > oldGroupIndex ? targetGroupIndex - 1 : targetGroupIndex;
            insertIndex = Mathf.Clamp(insertIndex, 0, UiSessionState.ModGroups.Count);
            UiSessionState.ModGroups.Insert(insertIndex, group);

            // 2. 物理 SiblingIndex 规整对齐
            if (!PrefsStorage.UseInMemoryPrefsForTests)
            {
                ApplyPhysicalSiblingIndexAndLayoutNoInline(containerRt);
            }

            // 3. 内存待定态：若此前不是 CustomOrder，无缝切换为 CustomOrder 并标记非用户主动选下拉框
            if (UiSessionState.CurrentCardSortMode != ModCardSortMode.CustomOrder)
            {
                UiSessionState.CurrentCardSortMode = ModCardSortMode.CustomOrder;
                UiSessionState.IsSortModeExplicitlyChosenByUser = false;
            }

            // 4. 5 大防假脏边界检测：若拖回原位与 BaselineVisualModIdOrder 逐项相等，自动恢复并清除脏标记
            UiSessionState.CheckCardOrderDirtyState();
            if (!PrefsStorage.UseInMemoryPrefsForTests)
            {
                UnifiedOptionWindow.SyncCardSortDropdownAppearance();
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void ApplyPhysicalSiblingIndexAndLayoutNoInline(RectTransform? containerRt)
        {
            try
            {
                for (int i = 0; i < UiSessionState.ModGroups.Count; i++)
                {
                    var g = UiSessionState.ModGroups[i];
                    if (UiSessionState.TryGetCardParts(g, out var parts) && !object.ReferenceEquals(parts.CardRootObject, null))
                    {
                        parts.CardRootObject.transform.SetSiblingIndex(i);
                    }
                }

                var rt = containerRt ?? UiSessionState.ModCardsContainer?.GetComponent<RectTransform>();
                if (rt != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
                }
            }
            catch
            {
            }
        }
    }
}
