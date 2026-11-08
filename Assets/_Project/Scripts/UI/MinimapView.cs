using System.Collections.Generic;
using P1.Core;
using P1.Environment;
using P1.Ink;
using P1.Player;
using P1.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 实时浮雕小地图视图（Minimap View，P4-D）：
    /// 借鉴 Inkwave .iw-map 潮流设计：
    /// 1. 圆角浮雕黑边外框 + 白色反光玻璃光罩 (Gloss) + 右上角 "MAP" 潮牌胶囊角标；
    /// 2. 墨水地面贴图（双线性滤波，实时刷新）；
    /// 3. 本地主角专属朝向箭头指示 + 动态雷达脉冲光环；
    /// 4. 队友圆点实时跟踪（存活队伍色 / 阵亡灰十字）；
    /// 5. 敌人探出标记（潜墨隐匿过滤，敌人潜行静止时完全隐匿防透视）；
    /// 6. 危险预警圈（手雷引信红圈 + 超级降落 / 风暴云范围圈）。
    /// </summary>
    [AddComponentMenu("P1/UI/Minimap View")]
    [RequireComponent(typeof(RectTransform))]
    public class MinimapView : MonoBehaviour
    {
        [Header("主题与配置")]
        [SerializeField] private UIThemeSO theme;
        // 40×56 地图严格 5:7（240×336），消除世界比例被横向拉伸；
        // 内容区再按 5:7 反推上下内边距，标记坐标与世界坐标一一对应。
        [SerializeField] private Vector2 mapPixelSize = new Vector2(240f, 336f);
        [SerializeField] private float cornerRadius = 26f;

        [Header("外框与核心贴图")]
        [SerializeField] private UIInkShapeGraphic frameGraphic;
        [SerializeField] private RawImage minimapRawImage;
        [SerializeField] private RectTransform mapContentContainer;
        [SerializeField] private RectTransform glossOverlay;
        [SerializeField] private RectTransform mapBadge;

        [Header("标记图层")]
        [SerializeField] private RectTransform markersContainer;
        [SerializeField] private RectTransform playerMarker;
        [SerializeField] private RectTransform playerArrow;
        [SerializeField] private RectTransform playerPulseRing;

        private Image _playerArrowImage;
        private Image _playerPulseRingImage;
        private readonly Dictionary<CombatantHealth, InkCharacterMotor> _motorCache = new Dictionary<CombatantHealth, InkCharacterMotor>(8);
        private readonly List<CombatantHealth> _deadHealthBuffer = new List<CombatantHealth>(8);

        private readonly List<ActorMarkerItem> _allyMarkers = new List<ActorMarkerItem>(4);
        private readonly List<ActorMarkerItem> _enemyMarkers = new List<ActorMarkerItem>(4);
        private readonly List<HazardMarkerItem> _hazardMarkers = new List<HazardMarkerItem>(6);
        /// <summary>风暴云快照复用列表（避免每帧分配）。</summary>
        private readonly List<StormCloudInfo> _stormInfoScratch = new List<StormCloudInfo>(8);

        public const int MaxFlashPoolSize = 64;
        private readonly FlashMarkerItem[] _flashPool = new FlashMarkerItem[MaxFlashPoolSize];
        private readonly List<MinimapFlashEvent> _pendingEvents = new List<MinimapFlashEvent>(MaxFlashPoolSize);
        private float _lastFlashFlushTime = -10f;
        private int _activeFlashCount;
        private int _lastFlashUpdateFrame = -1;
        private Canvas[] _visibilityCanvasChain;
        private bool _hiddenFlashCleared;

        private InkSurfaceController _subscribedSurface;
        private MatchManager _subscribedMatch;

        private class ActorMarkerItem
        {
            public RectTransform Root;
            public Image DotImage;
            public Image CrossImage;
            public CombatantHealth BoundHealth;
            public InkCharacterMotor BoundMotor;
            public bool IsAlly;
        }

        private class HazardMarkerItem
        {
            public RectTransform Root;
            public Image DiscImage;
            public Image FillImage;
            public Image RingImage;
            public Image IconImage;
            public bool IsActive;
        }

        private class FlashMarkerItem
        {
            public RectTransform Root;
            public Image Image;
            public bool IsActive;
            public float SpawnTime;
            public float Lifetime;
            public Color BaseColor;
        }

        private float _pulseTimer;
        private bool _layoutNormalized;
        private static Sprite _circleSprite;
        private static Sprite _arrowSprite;
        private static Sprite _ringSprite;

        public RectTransform PlayerMarker => playerMarker;
        public int AllyMarkerCount => _allyMarkers.Count;
        public int EnemyMarkerCount => _enemyMarkers.Count;
        public int FlashPoolSize => MaxFlashPoolSize;
        public int ActiveFlashCount => _activeFlashCount;
        public int PendingFlashCount => _pendingEvents.Count;

        public void SetTheme(UIThemeSO themeAsset)
        {
            if (themeAsset != null) theme = themeAsset;
        }

        private void Awake()
        {
            EnsureBuilt();
        }

        public void EnsureBuilt()
        {
            var rt = GetComponent<RectTransform>();
            if (rt == null) rt = gameObject.AddComponent<RectTransform>();
            rt.sizeDelta = mapPixelSize;

            // 右上角锚定（轴心右上），四周留 20px 屏边距：
            // 面板档硬影 (4,-5) 画在 Rect 右下之外也不会被屏幕边缘裁掉。
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            if (!_layoutNormalized)
            {
                _layoutNormalized = true;
                rt.anchoredPosition = new Vector2(-20f, -20f);
            }

            // 旧场景烘焙的「实时全图涂地小地图」标题与新 MAP 徽章重复，按名字找到即关（找不到不报错）
            Transform legacyTitle = transform.Find("MinimapTitle");
            if (legacyTitle != null && legacyTitle.gameObject.activeSelf)
            {
                legacyTitle.gameObject.SetActive(false);
            }

            UIThemeSO resolved = UIThemeSO.Resolve(theme);

            // 1. 圆角黑边外框（RoundedRect 自适应半径 + 4px 墨边 + 纯纸白底 + 面板档硬影）
            if (frameGraphic == null)
            {
                var bgGo = new GameObject("FrameBg", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                bgGo.transform.SetParent(transform, false);
                Stretch((RectTransform)bgGo.transform);
                frameGraphic = bgGo.GetComponent<UIInkShapeGraphic>();
                frameGraphic.Shape = UIInkShape.RoundedRect; // 圆角化
                frameGraphic.BorderWidth = ComicStyle.Border;
                frameGraphic.BorderColor = ComicStyle.Ink;
                frameGraphic.Halftone = false;
                frameGraphic.GlossAlpha = 0f;
                frameGraphic.DrawGloss = false;
                frameGraphic.InnerRimAlpha = 0f;
                frameGraphic.color = ComicStyle.Paper;
                frameGraphic.raycastTarget = false;
            }
            if (frameGraphic != null)
            {
                // 面板档硬影 (4,-5)
                resolved.ApplyHardShadow(frameGraphic, UIHardShadowLevel.Panel);
            }

            // 2. 墨水贴图容器（带直角/微圆角遮罩裁切）
            if (mapContentContainer == null)
            {
                var maskGo = new GameObject("MapMask", typeof(RectTransform), typeof(RectMask2D));
                maskGo.transform.SetParent(transform, false);
                mapContentContainer = maskGo.GetComponent<RectTransform>();
                ApplyMapContentInsets(mapContentContainer);

                var rawGo = new GameObject("MinimapRawImage", typeof(RectTransform), typeof(RawImage));
                rawGo.transform.SetParent(mapContentContainer, false);
                Stretch((RectTransform)rawGo.transform);
                minimapRawImage = rawGo.GetComponent<RawImage>();
                minimapRawImage.color = Color.white;
                minimapRawImage.raycastTarget = false;
            }

            // 3. 内部高光层（漫画模式关闭渐变）
            if (glossOverlay == null)
            {
                var glossGo = new GameObject("GlossOverlay", typeof(RectTransform), typeof(Image));
                glossGo.transform.SetParent(mapContentContainer, false);
                glossOverlay = glossGo.GetComponent<RectTransform>();
                Stretch(glossOverlay);
                var glossImg = glossGo.GetComponent<Image>();
                glossImg.color = Color.clear;
                glossImg.raycastTarget = false;
                glossGo.SetActive(false);
            }

            // 4. 标记层 MarkersContainer
            if (markersContainer == null)
            {
                var markersGo = new GameObject("MarkersContainer", typeof(RectTransform));
                markersGo.transform.SetParent(mapContentContainer, false);
                markersContainer = markersGo.GetComponent<RectTransform>();
                Stretch(markersContainer);
            }

            // 5. 本地主角指示标记（整体较旧版放大 1.25×：38px 底盘 / 42px 脉冲 / 24×26 箭头）
            if (playerMarker == null && markersContainer != null)
            {
                var pGo = new GameObject("PlayerMarker", typeof(RectTransform));
                pGo.transform.SetParent(markersContainer, false);
                playerMarker = pGo.GetComponent<RectTransform>();
                playerMarker.sizeDelta = new Vector2(38f, 38f);

                // 外层脉冲光环
                var pulseGo = new GameObject("PulseRing", typeof(RectTransform), typeof(Image));
                pulseGo.transform.SetParent(playerMarker, false);
                playerPulseRing = pulseGo.GetComponent<RectTransform>();
                playerPulseRing.sizeDelta = new Vector2(42f, 42f);
                var pImg = pulseGo.GetComponent<Image>();
                pImg.sprite = GetRingSprite();
                pImg.color = new Color(ComicStyle.Yellow.r, ComicStyle.Yellow.g, ComicStyle.Yellow.b, 0.85f);
                pImg.raycastTarget = false;

                // 朝向箭头/核心点
                var arrowGo = new GameObject("Arrow", typeof(RectTransform), typeof(Image));
                arrowGo.transform.SetParent(playerMarker, false);
                playerArrow = arrowGo.GetComponent<RectTransform>();
                playerArrow.sizeDelta = new Vector2(24f, 26f);
                var aImg = arrowGo.GetComponent<Image>();
                aImg.sprite = GetArrowSprite();
                aImg.color = Color.white;
                aImg.raycastTarget = false;
            }

            // 6. 右上角 MAP 直角标牌（纯黄底墨边 + 色签档硬影，标题字号 20）
            if (mapBadge == null)
            {
                var badgeGo = new GameObject("MapBadge", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                badgeGo.transform.SetParent(transform, false);
                mapBadge = badgeGo.GetComponent<RectTransform>();
                mapBadge.anchorMin = new Vector2(1f, 1f);
                mapBadge.anchorMax = new Vector2(1f, 1f);
                mapBadge.pivot = new Vector2(1f, 1f);
                mapBadge.anchoredPosition = new Vector2(4f, 6f);
                mapBadge.sizeDelta = new Vector2(72f, 32f);

                var badgeShape = badgeGo.GetComponent<UIInkShapeGraphic>();
                badgeShape.Shape = UIInkShape.Pill; // 圆角化：MAP 角签改胶囊
                badgeShape.BorderWidth = ComicStyle.BorderThin;
                badgeShape.BorderColor = ComicStyle.Ink;
                badgeShape.color = ComicStyle.Yellow;
                resolved.ApplyHardShadow(badgeShape, UIHardShadowLevel.Tag);

                var txtGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                txtGo.transform.SetParent(mapBadge, false);
                Stretch((RectTransform)txtGo.transform);
                var txt = txtGo.GetComponent<TextMeshProUGUI>();
                txt.font = resolved.ResolveFont(true, "MAP");
                txt.fontSize = UITypography.FontSnapped(20f);
                txt.fontStyle = FontStyles.Bold;
                txt.alignment = TextAlignmentOptions.Center;
                txt.color = ComicStyle.Ink;
                txt.text = "MAP";
                txt.raycastTarget = false;
            }

            // 7. 去掉胶带装饰
            BuildCornerTapes();

            BuildActorMarkerPools();
            BuildFlashPool();

            if (_playerArrowImage == null && playerArrow != null) _playerArrowImage = playerArrow.GetComponent<Image>();
            if (_playerPulseRingImage == null && playerPulseRing != null) _playerPulseRingImage = playerPulseRing.GetComponent<Image>();
        }

        private void BuildCornerTapes()
        {
            // 几何大胆风：去除胶带装饰
            string[] tapes = { "Tape_TL", "Tape_TR", "Tape_BL" };
            for (int i = 0; i < tapes.Length; i++)
            {
                var t = transform.Find(tapes[i]);
                if (t != null) Destroy(t.gameObject);
            }
        }

        /// <summary>
        /// 地图内容区按世界 40×56 = 5:7 反推内边距：
        /// 宽度方向固定 8px 白边，高度方向由 5:7 决定（240 宽 → 224×313.6，上下各约 11px），
        /// 这样标记的 px/m 在 X/Y 两轴一致，圆点不会走成椭圆轨迹。
        /// </summary>
        private void ApplyMapContentInsets(RectTransform content)
        {
            if (content == null) return;

            const float insetX = 8f;
            float width = Mathf.Max(40f, mapPixelSize.x - insetX * 2f);
            float aspect = ArenaLayoutV2.MapSizeZ / Mathf.Max(0.01f, ArenaLayoutV2.MapSizeX);
            float height = width * aspect;
            float insetY = Mathf.Max(insetX, (mapPixelSize.y - height) * 0.5f);

            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = new Vector2(insetX, insetY);
            content.offsetMax = new Vector2(-insetX, -insetY);
        }

        private void BuildActorMarkerPools()
        {
            if (markersContainer == null) return;

            // 预先建立 3 个队友标记和 4 个敌人标记
            while (_allyMarkers.Count < 3)
            {
                _allyMarkers.Add(CreateActorMarkerNode("AllyDot_" + _allyMarkers.Count, true));
            }

            while (_enemyMarkers.Count < 4)
            {
                _enemyMarkers.Add(CreateActorMarkerNode("EnemyDot_" + _enemyMarkers.Count, false));
            }

            while (_hazardMarkers.Count < 4)
            {
                _hazardMarkers.Add(CreateHazardMarkerNode("Hazard_" + _hazardMarkers.Count));
            }
        }

        private ActorMarkerItem CreateActorMarkerNode(string name, bool isAlly)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(markersContainer, false);
            var rt = go.GetComponent<RectTransform>();
            // 标记整体放大 1.25×：队友 20px、敌人 16px、阵亡叉 14px
            rt.sizeDelta = new Vector2(isAlly ? 20f : 16f, isAlly ? 20f : 16f);

            var img = go.GetComponent<Image>();
            img.sprite = GetCircleSprite();
            img.raycastTarget = false;

            // 阵亡十字标记
            var crossGo = new GameObject("Cross", typeof(RectTransform), typeof(Image));
            crossGo.transform.SetParent(rt, false);
            var crt = crossGo.GetComponent<RectTransform>();
            crt.sizeDelta = new Vector2(14f, 14f);
            var cImg = crossGo.GetComponent<Image>();
            cImg.color = ComicStyle.Ink;
            cImg.raycastTarget = false;
            crossGo.SetActive(false);

            go.SetActive(false);

            return new ActorMarkerItem
            {
                Root = rt,
                DotImage = img,
                CrossImage = cImg,
                IsAlly = isAlly
            };
        }

        private HazardMarkerItem CreateHazardMarkerNode(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(markersContainer, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(30f, 30f);

            // P41 三层读法（对齐 Inkwave minimap 风暴盘）：半透明范围盘 + 剩余寿命倒计时弧 + 闪烁边环
            var discGo = new GameObject("Disc", typeof(RectTransform), typeof(Image));
            discGo.transform.SetParent(rt, false);
            StretchMarkerChild(discGo.GetComponent<RectTransform>());
            var discImg = discGo.GetComponent<Image>();
            discImg.sprite = GetCircleSprite();
            discImg.raycastTarget = false;

            var fillGo = new GameObject("Countdown", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(rt, false);
            StretchMarkerChild(fillGo.GetComponent<RectTransform>());
            var fillImg = fillGo.GetComponent<Image>();
            fillImg.sprite = GetCircleSprite();
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Radial360;
            fillImg.fillOrigin = (int)Image.Origin360.Top;
            fillImg.fillAmount = 1f;
            fillImg.raycastTarget = false;

            var ringGo = new GameObject("Ring", typeof(RectTransform), typeof(Image));
            ringGo.transform.SetParent(rt, false);
            StretchMarkerChild(ringGo.GetComponent<RectTransform>());
            var ringImg = ringGo.GetComponent<Image>();
            ringImg.sprite = GetRingSprite();
            ringImg.raycastTarget = false;

            go.SetActive(false);

            return new HazardMarkerItem
            {
                Root = rt,
                DiscImage = discImg,
                FillImage = fillImg,
                RingImage = ringImg,
                IsActive = false
            };
        }

        private static void StretchMarkerChild(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private void BuildFlashPool()
        {
            if (markersContainer == null) return;

            for (int i = 0; i < MaxFlashPoolSize; i++)
            {
                if (_flashPool[i] == null || _flashPool[i].Root == null)
                {
                    _flashPool[i] = CreateFlashMarkerNode("FlashDot_" + i);
                }
            }
        }

        private FlashMarkerItem CreateFlashMarkerNode(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(markersContainer, false);
            // 保持闪光在角色/危险标记层下方，地块上方
            go.transform.SetAsFirstSibling();

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(6f, 6f);

            var img = go.GetComponent<Image>();
            img.sprite = GetCircleSprite();
            img.raycastTarget = false;
            img.color = new Color(1f, 1f, 1f, 0f);

            go.SetActive(false);

            return new FlashMarkerItem
            {
                Root = rt,
                Image = img,
                IsActive = false,
                SpawnTime = -1f,
                Lifetime = MinimapFlashMath.DefaultLifetime,
                BaseColor = Color.white
            };
        }

        private void OnEnable()
        {
            EnsureBuilt();
            UpdateInkSubscription();
            UpdateMatchSubscription();
        }

        private void OnDisable()
        {
            UnsubscribeInkEvents();
            UnsubscribeMatchPhase();
            ClearAllFlashes();
        }

        private void OnDestroy()
        {
            UnsubscribeInkEvents();
            UnsubscribeMatchPhase();
            ClearAllFlashes();
            _motorCache.Clear();
            _deadHealthBuffer.Clear();
        }

        private void Update()
        {
            UpdateInkSubscription();
            UpdateMatchSubscription();
            CheckMatchPhaseStatus();

            // HUD 隐藏期间不驱动墨渍闪烁：清一次残留，避免重新显示时闪现旧墨点。
            if (!UICanvasVisibility.IsVisible(this, ref _visibilityCanvasChain))
            {
                if (!_hiddenFlashCleared)
                {
                    _hiddenFlashCleared = true;
                    ClearAllFlashes();
                }
                return;
            }
            _hiddenFlashCleared = false;

            // 如果本帧尚未由 UpdateMinimap 驱动（例如独立测试或非 HudController 托管环境），在此处补全更新
            if (_lastFlashUpdateFrame != Time.frameCount)
            {
                Vector2 origin;
                Vector2 mapSize;
                var surface = InkSurfaceController.Instance;
                if (surface != null && surface.Grid != null)
                {
                    origin = surface.Grid.OriginXZ;
                    mapSize = surface.Grid.MapSize;
                }
                else
                {
                    origin = new Vector2(-ArenaLayoutV2.HalfX, -ArenaLayoutV2.HalfZ);
                    mapSize = ArenaLayoutV2.MapSize;
                }
                Vector2 containerSize = mapContentContainer != null ? mapContentContainer.rect.size : mapPixelSize;
                UpdateFlashLayer(origin, mapSize, containerSize);
            }
        }

        private void UpdateInkSubscription()
        {
            var current = InkSurfaceController.Instance;
            if (_subscribedSurface != current)
            {
                if (_subscribedSurface != null)
                {
                    _subscribedSurface.OnInkPainted -= HandleInkPainted;
                    _subscribedSurface = null;
                }
                if (current != null && isActiveAndEnabled)
                {
                    current.OnInkPainted += HandleInkPainted;
                    _subscribedSurface = current;
                }
            }
        }

        private void UnsubscribeInkEvents()
        {
            if (_subscribedSurface != null)
            {
                _subscribedSurface.OnInkPainted -= HandleInkPainted;
                _subscribedSurface = null;
            }
        }

        private void UpdateMatchSubscription()
        {
            var current = MatchManager.Instance;
            if (_subscribedMatch != current)
            {
                if (_subscribedMatch != null)
                {
                    _subscribedMatch.PhaseChanged -= HandleMatchPhaseChanged;
                    _subscribedMatch = null;
                }
                if (current != null && isActiveAndEnabled)
                {
                    current.PhaseChanged += HandleMatchPhaseChanged;
                    _subscribedMatch = current;
                }
            }
        }

        private void UnsubscribeMatchPhase()
        {
            if (_subscribedMatch != null)
            {
                _subscribedMatch.PhaseChanged -= HandleMatchPhaseChanged;
                _subscribedMatch = null;
            }
        }

        private void HandleMatchPhaseChanged(MatchManager.Phase phase)
        {
            if (phase == MatchManager.Phase.Ready || phase == MatchManager.Phase.Result)
            {
                ClearAllFlashes();
            }
        }

        private bool IsMatchInactive()
        {
            var match = _subscribedMatch != null ? _subscribedMatch : MatchManager.Instance;
            if (match != null)
            {
                var phase = match.CurrentPhase;
                return phase == MatchManager.Phase.Ready || phase == MatchManager.Phase.Result;
            }
            return false;
        }

        private void CheckMatchPhaseStatus()
        {
            if (IsMatchInactive())
            {
                if (_activeFlashCount > 0 || _pendingEvents.Count > 0)
                {
                    ClearAllFlashes();
                }
            }
        }

        private void HandleInkPainted(Vector3 worldPos, float radius, Team team, int changedCells)
        {
            if (changedCells <= 0 || team == Team.Neutral) return;
            if (IsMatchInactive()) return;

            float now = Time.time;
            var evt = new MinimapFlashEvent(worldPos, team, now, radius);
            MinimapFlashMath.TryAddWithDeduplication(
                _pendingEvents,
                evt,
                MaxFlashPoolSize,
                MinimapFlashMath.DefaultMergeDistance,
                MinimapFlashMath.DefaultDedupeWindow);
        }

        private void UpdateFlashLayer(Vector2 origin, Vector2 mapSize, Vector2 containerSize)
        {
            _lastFlashUpdateFrame = Time.frameCount;

            if (IsMatchInactive())
            {
                if (_activeFlashCount > 0 || _pendingEvents.Count > 0)
                {
                    ClearAllFlashes();
                }
                return;
            }

            float now = Time.time;

            // 1. 6Hz 节流批量刷新
            if (MinimapFlashMath.ShouldFlush(now, _lastFlashFlushTime, MinimapFlashMath.ThrottleInterval))
            {
                _lastFlashFlushTime = now;
                if (_pendingEvents.Count > 0)
                {
                    FlushPendingFlashes(origin, mapSize, containerSize, now);
                }
            }

            // 2. 衰减已有活跃闪光
            if (_activeFlashCount > 0)
            {
                for (int i = 0; i < MaxFlashPoolSize; i++)
                {
                    var item = _flashPool[i];
                    if (item == null || !item.IsActive) continue;

                    float elapsed = now - item.SpawnTime;
                    float alpha = MinimapFlashMath.EvaluateAlpha(elapsed, item.Lifetime, MinimapFlashMath.InitialAlpha);

                    if (alpha <= 0.001f || elapsed >= item.Lifetime)
                    {
                        item.IsActive = false;
                        _activeFlashCount--;
                        if (item.Root != null) item.Root.gameObject.SetActive(false);
                    }
                    else
                    {
                        if (item.Image != null)
                        {
                            Color c = item.BaseColor;
                            item.Image.color = new Color(c.r, c.g, c.b, alpha);
                        }
                    }
                }
            }
        }

        private void FlushPendingFlashes(Vector2 origin, Vector2 mapSize, Vector2 containerSize, float now)
        {
            var surface = InkSurfaceController.Instance;
            int pendingCount = _pendingEvents.Count;

            for (int p = 0; p < pendingCount; p++)
            {
                var evt = _pendingEvents[p];
                var item = GetOrCreateAvailableFlashSlot();
                if (item == null) continue;

                Vector2 pos = MinimapFlashMath.WorldToMapAnchoredPosition(evt.WorldPos, origin, mapSize, containerSize);
                item.Root.anchoredPosition = pos;

                // 尺寸 4~7px：根据世界半径适度映射，严格限制在 4~7px 范围内
                float mapR = MinimapMath.WorldRadiusToMapRadius(evt.Radius, mapSize, containerSize);
                float size = evt.Radius > 0f ? Mathf.Clamp(mapR * 2f, 4f, 7f) : 5.5f;
                item.Root.sizeDelta = new Vector2(size, size);

                Color teamCol;
                if (surface != null)
                {
                    teamCol = surface.GetTeamColor(evt.Team);
                }
                else
                {
                    teamCol = evt.Team == Team.TeamA
                        ? new Color(1.00f, 0.44f, 0.06f, 1f)
                        : new Color(0.15f, 0.42f, 1.00f, 1f);
                }

                item.BaseColor = teamCol;
                item.SpawnTime = now;
                item.Lifetime = MinimapFlashMath.DefaultLifetime;

                if (!item.IsActive)
                {
                    item.IsActive = true;
                    _activeFlashCount++;
                }

                if (item.Image != null)
                {
                    item.Image.color = new Color(teamCol.r, teamCol.g, teamCol.b, MinimapFlashMath.InitialAlpha);
                }
                item.Root.gameObject.SetActive(true);
            }

            _pendingEvents.Clear();
        }

        private FlashMarkerItem GetOrCreateAvailableFlashSlot()
        {
            float oldestTime = float.MaxValue;
            int oldestIndex = -1;

            for (int i = 0; i < MaxFlashPoolSize; i++)
            {
                var item = _flashPool[i];
                if (item == null) continue;

                if (!item.IsActive)
                {
                    return item;
                }

                if (item.SpawnTime < oldestTime)
                {
                    oldestTime = item.SpawnTime;
                    oldestIndex = i;
                }
            }

            if (oldestIndex >= 0)
            {
                return _flashPool[oldestIndex];
            }

            return null;
        }

        public void ClearAllFlashes()
        {
            _pendingEvents.Clear();
            _activeFlashCount = 0;
            for (int i = 0; i < MaxFlashPoolSize; i++)
            {
                var item = _flashPool[i];
                if (item != null)
                {
                    item.IsActive = false;
                    if (item.Root != null)
                    {
                        item.Root.gameObject.SetActive(false);
                    }
                }
            }
        }

        public void RecordFlash(Vector3 worldPos, float radius, Team team, int changedCells = 1)
        {
            HandleInkPainted(worldPos, radius, team, changedCells);
        }

        public void FlushFlashesImmediate()
        {
            Vector2 origin;
            Vector2 mapSize;
            var surface = InkSurfaceController.Instance;
            if (surface != null && surface.Grid != null)
            {
                origin = surface.Grid.OriginXZ;
                mapSize = surface.Grid.MapSize;
            }
            else
            {
                origin = new Vector2(-ArenaLayoutV2.HalfX, -ArenaLayoutV2.HalfZ);
                mapSize = ArenaLayoutV2.MapSize;
            }
            Vector2 containerSize = mapContentContainer != null ? mapContentContainer.rect.size : mapPixelSize;
            FlushPendingFlashes(origin, mapSize, containerSize, Time.time);
        }

        public void UpdateMinimap()
        {
            var surface = InkSurfaceController.Instance;
            if (surface == null || surface.Grid == null) return;

            // 1. 同步墨水贴图
            if (minimapRawImage != null && minimapRawImage.texture != surface.InkTexture)
            {
                minimapRawImage.texture = surface.InkTexture;
            }

            Vector2 origin = surface.Grid.OriginXZ;
            Vector2 mapSize = surface.Grid.MapSize;
            Vector2 containerSize = mapContentContainer != null ? mapContentContainer.rect.size : mapPixelSize;

            UIThemeSO resolved = UIThemeSO.Resolve(theme);

            // 2. 更新本地玩家
            var localPlayer = PlayerController.LocalPlayer;
            if (localPlayer != null && playerMarker != null)
            {
                Vector3 pPos = localPlayer.transform.position;
                Vector2 mapPos = MinimapMath.WorldToMapAnchoredPosition(pPos, origin, mapSize, containerSize);
                playerMarker.anchoredPosition = mapPos;

                if (playerArrow != null)
                {
                    Vector3 aimDir = localPlayer.AimGroundPoint - pPos;
                    if (aimDir.sqrMagnitude > 0.01f)
                    {
                        float angle = Mathf.Atan2(aimDir.z, aimDir.x) * Mathf.Rad2Deg - 90f;
                        playerArrow.localRotation = Quaternion.Euler(0f, 0f, angle);
                    }
                    if (_playerArrowImage == null) _playerArrowImage = playerArrow.GetComponent<Image>();
                    if (_playerArrowImage != null) _playerArrowImage.color = resolved.GetTeamColor(localPlayer.Health.Team);
                }

                _pulseTimer += Time.deltaTime * 2.2f;
                if (playerPulseRing != null)
                {
                    float pScale = 1.0f + (Mathf.Repeat(_pulseTimer, 1f) * 0.8f);
                    float pAlpha = 1f - Mathf.Repeat(_pulseTimer, 1f);
                    playerPulseRing.localScale = new Vector3(pScale, pScale, 1f);
                    if (_playerPulseRingImage == null) _playerPulseRingImage = playerPulseRing.GetComponent<Image>();
                    if (_playerPulseRingImage != null) _playerPulseRingImage.color = new Color(1f, 1f, 1f, pAlpha * 0.65f);
                }
            }

            // 3. 更新所有队友与敌人标记
            UpdateCombatantMarkers(origin, mapSize, containerSize, resolved);

            // 4. 更新危险预警圈（手雷与大招）
            UpdateHazardMarkers(origin, mapSize, containerSize);

            // 5. 更新占领闪光图层（Flash Layer，对标 INKWAVE）
            UpdateFlashLayer(origin, mapSize, containerSize);
        }

        private void UpdateCombatantMarkers(Vector2 origin, Vector2 mapSize, Vector2 containerSize, UIThemeSO resolved)
        {
            var all = CombatantHealthRegistry.All;
            if (all == null) return;

            if (_motorCache.Count > 16)
            {
                _deadHealthBuffer.Clear();
                foreach (var kvp in _motorCache)
                {
                    if (kvp.Key == null) _deadHealthBuffer.Add(kvp.Key);
                }
                for (int k = 0; k < _deadHealthBuffer.Count; k++)
                {
                    _motorCache.Remove(_deadHealthBuffer[k]);
                }
                _deadHealthBuffer.Clear();
            }

            var localPlayer = PlayerController.LocalPlayer;
            CombatantHealth localHealth = localPlayer != null ? localPlayer.Health : null;
            Team myTeam = localHealth != null ? localHealth.Team : Team.TeamA;

            int allyIndex = 0;
            int enemyIndex = 0;

            for (int i = 0; i < all.Count; i++)
            {
                var ch = all[i];
                if (ch == null || ch == localHealth) continue;

                if (!_motorCache.TryGetValue(ch, out var motor))
                {
                    motor = ch.GetComponent<InkCharacterMotor>();
                    _motorCache[ch] = motor;
                }

                bool isAlly = ch.Team == myTeam;

                if (isAlly)
                {
                    if (allyIndex < _allyMarkers.Count)
                    {
                        var item = _allyMarkers[allyIndex++];
                        item.BoundHealth = ch;
                        item.BoundMotor = motor;

                        Vector2 pos = MinimapMath.WorldToMapAnchoredPosition(ch.transform.position, origin, mapSize, containerSize);
                        item.Root.anchoredPosition = pos;
                        item.Root.gameObject.SetActive(true);

                        bool alive = ch.IsAlive;
                        if (item.DotImage != null)
                        {
                            item.DotImage.color = alive ? resolved.GetTeamColor(ch.Team) : new Color(0.4f, 0.4f, 0.4f, 0.8f);
                        }
                        if (item.CrossImage != null)
                        {
                            item.CrossImage.gameObject.SetActive(!alive);
                        }
                    }
                }
                else
                {
                    // 潜墨隐匿过滤：处于 Stealth 的敌人完全隐藏
                    bool show = MinimapMath.ShouldShowEnemy(ch, motor);
                    if (enemyIndex < _enemyMarkers.Count)
                    {
                        var item = _enemyMarkers[enemyIndex++];
                        item.BoundHealth = ch;
                        item.BoundMotor = motor;

                        if (show)
                        {
                            Vector2 pos = MinimapMath.WorldToMapAnchoredPosition(ch.transform.position, origin, mapSize, containerSize);
                            item.Root.anchoredPosition = pos;
                            item.Root.gameObject.SetActive(true);

                            if (item.DotImage != null)
                            {
                                item.DotImage.color = resolved.GetTeamColor(ch.Team);
                            }
                        }
                        else
                        {
                            item.Root.gameObject.SetActive(false);
                        }
                    }
                }
            }

            // 关闭多余节点
            for (int i = allyIndex; i < _allyMarkers.Count; i++) _allyMarkers[i].Root.gameObject.SetActive(false);
            for (int i = enemyIndex; i < _enemyMarkers.Count; i++) _enemyMarkers[i].Root.gameObject.SetActive(false);
        }

        private void UpdateHazardMarkers(Vector2 origin, Vector2 mapSize, Vector2 containerSize)
        {
            int hIndex = 0;

            // 1. 风暴云大招：全部云（含飞行中的发生器），队色盘 + 倒计时弧 + 告警边环
            if (InkStormCloudPool.Instance != null)
            {
                int stormCount = InkStormCloudPool.Instance.TryGetActiveClouds(_stormInfoScratch);
                for (int i = 0; i < stormCount && hIndex < _hazardMarkers.Count; i++)
                {
                    StormCloudInfo info = _stormInfoScratch[i];
                    var item = _hazardMarkers[hIndex++];
                    Vector2 pos = MinimapMath.WorldToMapAnchoredPosition(info.Position, origin, mapSize, containerSize);
                    item.Root.anchoredPosition = pos;
                    float mapR = MinimapMath.WorldRadiusToMapRadius(info.Radius, mapSize, containerSize);
                    item.Root.sizeDelta = new Vector2(mapR * 2f, mapR * 2f);

                    Color c = info.Spawned && InkSurfaceController.Instance != null
                        ? InkSurfaceController.Instance.GetTeamColor(info.Team)
                        : new Color(0.2f, 0.85f, 1f);
                    if (item.DiscImage != null)
                    {
                        item.DiscImage.color = new Color(c.r, c.g, c.b, info.Spawned ? 0.22f : 0f);
                    }
                    if (item.FillImage != null)
                    {
                        // 倒计时弧：寿命比例随时间收窄，剩余越少弧越短
                        item.FillImage.color = new Color(c.r, c.g, c.b, info.Spawned ? 0.30f : 0f);
                        item.FillImage.fillAmount = Mathf.Clamp01(info.Remaining01);
                    }
                    if (item.RingImage != null)
                    {
                        // 风暴云危险圈属功能性告警：只允许硬切闪烁（禁用正弦呼吸）
                        float blink = Mathf.Repeat(Time.time * 3f, 1f) < 0.5f ? 1f : 0.55f;
                        item.RingImage.color = new Color(c.r, c.g, c.b, blink);
                    }
                    item.Root.gameObject.SetActive(true);
                }
            }

            for (int i = hIndex; i < _hazardMarkers.Count; i++)
            {
                _hazardMarkers[i].Root.gameObject.SetActive(false);
            }
        }

        private static Sprite GetCircleSprite()
        {
            if (_circleSprite != null) return _circleSprite;

            int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "MinimapCircleTex" };
            Color[] cols = new Color[size * size];
            Vector2 c = new Vector2(size * 0.5f - 0.5f, size * 0.5f - 0.5f);
            float r = size * 0.5f - 1.2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), c);
                    float a = d <= r ? 1f : 0f;
                    cols[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(cols);
            tex.Apply();
            _circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return _circleSprite;
        }

        private static Sprite GetRingSprite()
        {
            if (_ringSprite != null) return _ringSprite;

            int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "MinimapRingTex" };
            Color[] cols = new Color[size * size];
            Vector2 c = new Vector2(size * 0.5f - 0.5f, size * 0.5f - 0.5f);
            float r = 28f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), c);
                    float diff = Mathf.Abs(d - r);
                    float a = diff <= 2.5f ? 1f : 0f;
                    cols[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(cols);
            tex.Apply();
            _ringSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return _ringSprite;
        }

        private static Sprite GetArrowSprite()
        {
            if (_arrowSprite != null) return _arrowSprite;

            int w = 32, h = 36;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "MinimapArrowTex" };
            Color[] cols = new Color[w * h];

            // 绘制向上箭头三角形
            for (int y = 0; y < h; y++)
            {
                float t = y / (float)h;
                float halfWidth = Mathf.Lerp(14f, 0f, t);
                for (int x = 0; x < w; x++)
                {
                    float dx = Mathf.Abs(x - 15.5f);
                    float a = dx <= halfWidth ? 1f : 0f;
                    cols[y * w + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(cols);
            tex.Apply();
            _arrowSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
            return _arrowSprite;
        }

        private static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
