using System.Collections;
using NUnit.Framework;
using P1.Core;
using P1.Ink;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace P1.Tests
{
    /// <summary>
    /// 墨迹图集的运行时验收：落墨必须真的写进 RenderTexture
    /// （R=队伍占比、G=最新落墨时间戳、A=覆盖率），否则地面会出现「看起来没涂地」的静默故障。
    ///
    /// 表示法说明（墨迹图集烘干/生长 Pass 改片元计算）：
    /// 旧版 G 通道存「湿润度」并由 15Hz 全屏烘干 Pass 逐帧衰减；新版 G 存
    /// 「最新落墨时间戳 × 覆盖率」（预乘），湿润度由 InkGround 片元反预乘后按
    /// (_InkWetClockNorm - G/A) 现算，图集在墨变干的全过程保持不变。
    /// 因此湿润度断言改为「回读时间戳后用与 shader 相同的公式现算」——
    /// 烘干后墨迹行为仍然可观测（时间戳差 → 湿度衰减），测试意图不变。
    /// </summary>
    public class InkAtlasRenderPlayTests
    {
        /// <summary>与 InkSplatAtlas.DefaultWetClockPeriod / InkGridConfigSO.atlasWetClockPeriod 默认值一致。</summary>
        private const float WetClockPeriod = 32f;

        private GameObject _surfaceGo;
        private InkGridConfigSO _config;

        [TearDown]
        public void TearDown()
        {
            if (_surfaceGo != null)
            {
                Object.Destroy(_surfaceGo);
                _surfaceGo = null;
            }

            if (_config != null)
            {
                Object.Destroy(_config);
                _config = null;
            }
        }

        private InkSurfaceController CreateSurface(float dryingSeconds = 6f)
        {
            _config = ScriptableObject.CreateInstance<InkGridConfigSO>();
            _config.mapSize = new Vector2(40f, 40f);
            _config.desktopResolution = 100;
            _config.mobileResolution = 100;
            _config.inkAtlasResolutionDesktop = 256;
            _config.inkAtlasResolutionMobile = 256;
            _config.atlasMipmaps = false;
            _config.splatGrowSeconds = 0f;
            _config.inkDryingSeconds = dryingSeconds;

            // 先以未激活状态装配，避免 Awake 用默认配置先建一张 4096 图集
            _surfaceGo = new GameObject("InkSurfaceUnderTest");
            _surfaceGo.SetActive(false);
            var surface = _surfaceGo.AddComponent<InkSurfaceController>();
            surface.Configure(_config, new Renderer[0], new Collider[0]);
            _surfaceGo.SetActive(true);
            return surface;
        }

        private static Color ReadAtlasAt(InkSurfaceController surface, float worldX, float worldZ)
        {
            RenderTexture rt = surface.SplatAtlas.Atlas;
            Vector2 origin = surface.SplatAtlas.OriginXZ;
            Vector2 size = surface.SplatAtlas.MapSize;

            int px = Mathf.Clamp(Mathf.RoundToInt((worldX - origin.x) / size.x * rt.width), 0, rt.width - 1);
            int py = Mathf.Clamp(Mathf.RoundToInt((worldZ - origin.y) / size.y * rt.height), 0, rt.height - 1);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            tex.ReadPixels(new Rect(px, py, 1, 1), 0, 0);
            tex.Apply();
            Color c = tex.GetPixel(0, 0);

            RenderTexture.active = previous;
            Object.Destroy(tex);
            return c;
        }

        /// <summary>
        /// 与 InkGround 片元相同的湿润度公式：G 通道存「时间戳×覆盖率」（预乘），先反预乘，
        /// 再 age = (now − 时间戳×周期) mod 周期，湿度 = 1 − age/烘干秒数（线性，与旧烘干 Pass 等价）。
        /// </summary>
        private static float WetnessFromTimestamp(Color ink, float nowTime, float dryingSeconds)
        {
            float ts01 = Mathf.Clamp01(ink.g / Mathf.Max(0.02f, ink.a));
            float ageSec = Mathf.Repeat(nowTime - ts01 * WetClockPeriod, WetClockPeriod);
            return Mathf.Clamp01(1f - ageSec / Mathf.Max(0.25f, dryingSeconds));
        }

        /// <summary>圆环内的采样点必须也被涂到：防止「墨斑缩成一个像素」这类尺度错误只测圆心抓不到。</summary>
        private static void AssertBlobCoversRadius(InkSurfaceController surface, float radius, string label)
        {
            Color inner = ReadAtlasAt(surface, radius * 0.6f, 0f);
            Assert.Greater(inner.a, 0.9f, $"{label}: 半径 60% 处应被覆盖（A≈1），否则墨斑尺寸算错了");

            Color outside = ReadAtlasAt(surface, radius * 3.5f, 0f);
            Assert.Less(outside.a, 0.1f, $"{label}: 超出装饰半径（2.75R）之外不应有任何覆盖率");
        }

        [UnityTest]
        public IEnumerator PaintCircle_WritesTeamShareWetnessAndCoverage()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("无图形设备，无法验证 RenderTexture 内容");
            }

            InkSurfaceController surface = CreateSurface();
            yield return null;

            Assert.IsNotNull(surface.SplatAtlas, "墨迹图集应已创建");
            Assert.IsNotNull(surface.VisualInkTexture, "地面材质应绑定图集贴图");
            Assert.IsInstanceOf<Texture2D>(surface.InkTexture, "小地图用的 CPU 贴图必须保持 Texture2D 类型");

            surface.PaintCircle(Vector3.zero, 3f, Team.TeamA);
            yield return null;
            yield return null;

            Assert.Greater(surface.SplatAtlas.TotalSplatCount, 0, "落墨应记录到图集");

            float nowAtRead = Time.time;
            Color a = ReadAtlasAt(surface, 0f, 0f);
            Assert.Greater(a.a, 0.9f, "圆心覆盖率应接近 1（A 通道）");
            Assert.Less(a.r, 0.15f, "TeamA 的队伍占比应为 0（R 通道）");
            // G 通道 = 落墨时间戳×覆盖率：用与 InkGround 片元相同的公式现算湿润度（等价旧「G≈1 即湿润」断言）
            float wetA = WetnessFromTimestamp(a, nowAtRead, 6f);
            Assert.Greater(wetA, 0.9f, "刚落的墨按时间戳现算的湿润度应接近 1（G 通道存最新落墨时刻）");
            AssertBlobCoversRadius(surface, 3f, "TeamA 第一次落墨");

            // 新墨压旧墨：覆盖后圆心应完全变为 TeamB
            surface.PaintCircle(Vector3.zero, 3f, Team.TeamB);
            yield return null;
            yield return null;

            nowAtRead = Time.time;
            Color b = ReadAtlasAt(surface, 0f, 0f);
            Assert.Greater(b.r, 0.85f, "TeamB 覆盖后队伍占比应变为 1（R 通道）");
            Assert.Greater(b.a, 0.9f, "覆盖后覆盖率仍应接近 1");
            // Max 混合的时间戳应被新墨刷新：湿润度回到 ~1（最新墨斑获胜）
            float wetB = WetnessFromTimestamp(b, nowAtRead, 6f);
            Assert.Greater(wetB, 0.9f, "覆盖后新墨应刷新时间戳（湿润度回到 ~1）");
            AssertBlobCoversRadius(surface, 3f, "TeamB 覆盖后");
        }

        [UnityTest]
        public IEnumerator InkDrying_LowersTimestampWetnessOverTime_LeavesTeamAndCoverageIntact()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("无图形设备，无法验证 RenderTexture 内容");
            }

            InkSurfaceController surface = CreateSurface(dryingSeconds: 3f);
            yield return null;

            surface.PaintCircle(Vector3.zero, 3f, Team.TeamB);
            yield return null;
            yield return null;

            var beforeBuf = new Color[1];
            yield return ReadAtlasAtStable(surface, 0f, 0f, beforeBuf);
            Color before = beforeBuf[0];
            float wetBefore = WetnessFromTimestamp(before, Time.time, 3f);
            Assert.Greater(wetBefore, 0.9f, "刚落墨时按时间戳现算的湿润度应为 ~1");

            // 等待约 1.2s（3s 烘干周期的 40%）
            float waitUntil = Time.unscaledTime + 1.2f;
            while (Time.unscaledTime < waitUntil)
            {
                yield return null;
            }

            var afterBuf = new Color[1];
            yield return ReadAtlasAtStable(surface, 0f, 0f, afterBuf);
            Color after = afterBuf[0];
            float wetAfter = WetnessFromTimestamp(after, Time.time, 3f);
            Assert.Less(wetAfter, wetBefore - 0.15f, "按时间戳现算的湿润度应随时间衰减（等价旧烘干 Pass）");
            Assert.Greater(after.a, 0.95f, "烘干（时间戳表示）不得削减覆盖率（A 通道写入后不再被改写）");
            Assert.Greater(after.r, 0.85f, "烘干不得改动队伍归属（R 通道）");
            // 新表示法的带宽语义：没有周期性整图重绘，G 通道时间戳在烘干全程必须原样保留。
            // （旧实现 1.2s 内 G 会被烘干 Pass 削去约 0.2，该断言可同时抓住「退化回每帧重写」。）
            Assert.AreEqual(before.g, after.g, 0.02f, "G 通道时间戳应保持不变（图集不再被周期性重写）");
        }

        /// <summary>
        /// 后台不聚焦的编辑器里 RT 读回偶发全空（GPU 资源回收时序，见看板 P11-B 遗留观察）：
        /// 空读时多等几帧重试；最终仍为空则按最后一次读值正常断言，真实回归不会被掩盖。
        /// </summary>
        private static IEnumerator ReadAtlasAtStable(InkSurfaceController surface, float worldX, float worldZ,
            Color[] output)
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                output[0] = ReadAtlasAt(surface, worldX, worldZ);
                if (output[0].a > 0.01f)
                {
                    yield break;
                }

                yield return null;
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator ResetSurface_ClearsAtlas()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("无图形设备，无法验证 RenderTexture 内容");
            }

            InkSurfaceController surface = CreateSurface();
            yield return null;

            surface.PaintCircle(Vector3.zero, 3f, Team.TeamA);
            yield return null;
            yield return null;
            Assert.Greater(ReadAtlasAt(surface, 0f, 0f).a, 0.9f);

            surface.ResetSurface();
            yield return null;
            yield return null;

            Assert.Less(ReadAtlasAt(surface, 0f, 0f).a, 0.05f, "重开比赛后图集应被清空");
        }
    }
}
