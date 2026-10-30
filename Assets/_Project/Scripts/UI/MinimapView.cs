using System.Collections.Generic;
using P1.Core;
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
        // 40×56 地图的内容区约 5:7，避免把长轴世界压扁成正方形小地图。
        [SerializeField] private Vector2 mapPixelSize = new Vector2(168f, 230f);
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
            public Image RingImage;
            public Image IconImage;
            public bool IsActive;
        }

        private float _pulseTimer;
        private static Sprite _circleSprite;
        private static Sprite _arrowSprite;
        private static Sprite _ringSprite;

        public RectTransform PlayerMarker => playerMarker;
        public int AllyMarkerCount => _allyMarkers.Count;
        public int EnemyMarkerCount => _enemyMarkers.Count;

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

            UIThemeSO resolved = UIThemeSO.Resolve(theme);

            // 1. 漫画纸白画框 + 6px 墨边 + 硬阴影 (6,-6)
            if (frameGraphic == null)
            {
                var bgGo = new GameObject("FrameBg", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                bgGo.transform.SetParent(transform, false);
                Stretch((RectTransform)bgGo.transform);
                frameGraphic = bgGo.GetComponent<UIInkShapeGraphic>();
                frameGraphic.Shape = UIInkShape.RoundedRect;
                frameGraphic.CornerRadius = ComicStyle.CornerSharp;
                frameGraphic.BorderWidth = ComicStyle.Border;
                frameGraphic.BorderColor = ComicStyle.Ink;
                frameGraphic.HardShadow = true;
                frameGraphic.ShadowOffset = new Vector2(ComicStyle.ShadowOffset, -ComicStyle.ShadowOffset);
                frameGraphic.ShadowColor = ComicStyle.Ink;
                frameGraphic.Halftone = false;
                frameGraphic.GlossAlpha = 0f;
                frameGraphic.DrawGloss = false;
                frameGraphic.InnerRimAlpha = 0f;
                frameGraphic.color = ComicStyle.Paper;
                frameGraphic.raycastTarget = false;
            }

            // 2. 墨水贴图容器（带直角/微圆角遮罩裁切）
            if (mapContentContainer == null)
            {
                var maskGo = new GameObject("MapMask", typeof(RectTransform), typeof(RectMask2D));
                maskGo.transform.SetParent(transform, false);
                mapContentContainer = maskGo.GetComponent<RectTransform>();
                Stretch(mapContentContainer, 8f);

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

            // 5. 本地主角指示标记（放大到 8px 半径）
            if (playerMarker == null && markersContainer != null)
            {
                var pGo = new GameObject("PlayerMarker", typeof(RectTransform));
                pGo.transform.SetParent(markersContainer, false);
                playerMarker = pGo.GetComponent<RectTransform>();
                playerMarker.sizeDelta = new Vector2(30f, 30f);

                // 外层脉冲光环
                var pulseGo = new GameObject("PulseRing", typeof(RectTransform), typeof(Image));
                pulseGo.transform.SetParent(playerMarker, false);
                playerPulseRing = pulseGo.GetComponent<RectTransform>();
                playerPulseRing.sizeDelta = new Vector2(34f, 34f);
                var pImg = pulseGo.GetComponent<Image>();
                pImg.sprite = GetRingSprite();
                pImg.color = new Color(ComicStyle.Yellow.r, ComicStyle.Yellow.g, ComicStyle.Yellow.b, 0.85f);
                pImg.raycastTarget = false;

                // 朝向箭头/核心点
                var arrowGo = new GameObject("Arrow", typeof(RectTransform), typeof(Image));
                arrowGo.transform.SetParent(playerMarker, false);
                playerArrow = arrowGo.GetComponent<RectTransform>();
                playerArrow.sizeDelta = new Vector2(20f, 22f);
                var aImg = arrowGo.GetComponent<Image>();
                aImg.sprite = GetArrowSprite();
                aImg.color = Color.white;
                aImg.raycastTarget = false;
            }

            // 6. 右上角 MAP 胶囊角标（漫画黄底黑边）
            if (mapBadge == null)
            {
                var badgeGo = new GameObject("MapBadge", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                badgeGo.transform.SetParent(transform, false);
                mapBadge = badgeGo.GetComponent<RectTransform>();
                mapBadge.anchorMin = new Vector2(1f, 1f);
                mapBadge.anchorMax = new Vector2(1f, 1f);
                mapBadge.pivot = new Vector2(1f, 1f);
                mapBadge.anchoredPosition = new Vector2(6f, 8f);
                mapBadge.sizeDelta = new Vector2(50f, 22f);

                var badgeShape = badgeGo.GetComponent<UIInkShapeGraphic>();
                badgeShape.Shape = UIInkShape.RoundedRect;
                badgeShape.CornerRadius = ComicStyle.CornerSharp;
                badgeShape.BorderWidth = ComicStyle.BorderThin;
                badgeShape.BorderColor = ComicStyle.Ink;
                badgeShape.HardShadow = true;
                badgeShape.ShadowOffset = new Vector2(2.5f, -2.5f);
                badgeShape.ShadowColor = ComicStyle.Ink;
                badgeShape.color = ComicStyle.Yellow;

                var txtGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                txtGo.transform.SetParent(mapBadge, false);
                Stretch((RectTransform)txtGo.transform);
                var txt = txtGo.GetComponent<TextMeshProUGUI>();
                txt.font = resolved.ResolveFont(true, "MAP");
                txt.fontSize = 12f;
                txt.fontStyle = FontStyles.Bold;
                txt.alignment = TextAlignmentOptions.Center;
                txt.color = ComicStyle.Ink;
                txt.text = "MAP";
                txt.raycastTarget = false;
            }

            // 7. 角落黑色纸胶带条（三条小黑矩形，模拟纸面粘贴感）
            BuildCornerTapes();

            BuildActorMarkerPools();

            if (_playerArrowImage == null && playerArrow != null) _playerArrowImage = playerArrow.GetComponent<Image>();
            if (_playerPulseRingImage == null && playerPulseRing != null) _playerPulseRingImage = playerPulseRing.GetComponent<Image>();
        }

        private void BuildCornerTapes()
        {
            if (transform.Find("Tape_TL") != null) return;

            // 左上胶带
            CreateTape("Tape_TL", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f), 35f, new Vector2(30f, 10f));
            // 右上胶带
            CreateTape("Tape_TR", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), -35f, new Vector2(30f, 10f));
            // 左下胶带
            CreateTape("Tape_BL", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), -35f, new Vector2(28f, 10f));
        }

        private void CreateTape(string name, Vector2 anchor, Vector2 pivot, Vector2 pos, float rotation, Vector2 size)
        {
            var tapeGo = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            tapeGo.transform.SetParent(transform, false);
            var rt = tapeGo.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            rt.localRotation = Quaternion.Euler(0f, 0f, rotation);

            var shape = tapeGo.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.RoundedRect;
            shape.CornerRadius = 1.5f;
            shape.BorderWidth = 0f;
            shape.color = ComicStyle.Ink;
            shape.raycastTarget = false;
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
            // 标记适当放大（玩家 8px、敌人 6px 直径 12px，队友 16px）
            rt.sizeDelta = new Vector2(isAlly ? 16f : 12f, isAlly ? 16f : 12f);

            var img = go.GetComponent<Image>();
            img.sprite = GetCircleSprite();
            img.raycastTarget = false;

            // 阵亡十字标记
            var crossGo = new GameObject("Cross", typeof(RectTransform), typeof(Image));
            crossGo.transform.SetParent(rt, false);
            var crt = crossGo.GetComponent<RectTransform>();
            crt.sizeDelta = new Vector2(10f, 10f);
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
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(markersContainer, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(24f, 24f);

            var img = go.GetComponent<Image>();
            img.sprite = GetRingSprite();
            img.color = new Color(1f, 0.2f, 0.25f, 0.85f);
            img.raycastTarget = false;
            go.SetActive(false);

            return new HazardMarkerItem
            {
                Root = rt,
                RingImage = img,
                IsActive = false
            };
        }

        private void OnDestroy()
        {
            _motorCache.Clear();
            _deadHealthBuffer.Clear();
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

            // 1. 检查风暴云大招
            if (InkStormCloudPool.Instance != null && InkStormCloudPool.Instance.TryGetActiveCloudPosition(out Vector3 stormPos))
            {
                if (hIndex < _hazardMarkers.Count)
                {
                    var item = _hazardMarkers[hIndex++];
                    Vector2 pos = MinimapMath.WorldToMapAnchoredPosition(stormPos, origin, mapSize, containerSize);
                    item.Root.anchoredPosition = pos;
                    float mapR = MinimapMath.WorldRadiusToMapRadius(3.4f, mapSize, containerSize);
                    item.Root.sizeDelta = new Vector2(mapR * 2f, mapR * 2f);
                    if (item.RingImage != null)
                    {
                        float blink = 0.5f + Mathf.Sin(Time.time * 6f) * 0.35f;
                        item.RingImage.color = new Color(0.2f, 0.85f, 1f, blink);
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
                    float a = Mathf.Clamp01((r - d) * 1.8f);
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
                    float a = Mathf.Clamp01((3.5f - diff) * 1.2f);
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
