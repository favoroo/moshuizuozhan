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
    /// 墨迹图集的运行时验收：落墨必须真的写进 RenderTexture（R=队伍占比、G=湿润度、A=覆盖率），
    /// 否则地面会出现「看起来没涂地」的静默故障。
    /// </summary>
    public class InkAtlasRenderPlayTests
    {
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

            Color a = ReadAtlasAt(surface, 0f, 0f);
            Assert.Greater(a.a, 0.9f, "圆心覆盖率应接近 1（A 通道）");
            Assert.Less(a.r, 0.15f, "TeamA 的队伍占比应为 0（R 通道）");
            Assert.Greater(a.g, 0.9f, "刚落的墨应是湿润状态（G 通道）");
            AssertBlobCoversRadius(surface, 3f, "TeamA 第一次落墨");

            // 新墨压旧墨：覆盖后圆心应完全变为 TeamB
            surface.PaintCircle(Vector3.zero, 3f, Team.TeamB);
            yield return null;
            yield return null;

            Color b = ReadAtlasAt(surface, 0f, 0f);
            Assert.Greater(b.r, 0.85f, "TeamB 覆盖后队伍占比应变为 1（R 通道）");
            Assert.Greater(b.a, 0.9f, "覆盖后覆盖率仍应接近 1");
            AssertBlobCoversRadius(surface, 3f, "TeamB 覆盖后");
        }

        [UnityTest]
        public IEnumerator DryingPass_LowersWetnessOnly_LeavesTeamAndCoverageIntact()
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

            Color before = ReadAtlasAt(surface, 0f, 0f);
            Assert.Greater(before.g, 0.95f, "刚落墨时湿润度应为 1");

            // 等待约 1.2s（3s 烘干周期的 40%）
            float waitUntil = Time.unscaledTime + 1.2f;
            while (Time.unscaledTime < waitUntil)
            {
                yield return null;
            }

            Color after = ReadAtlasAt(surface, 0f, 0f);
            Assert.Less(after.g, before.g - 0.15f, "湿润度应随烘干 Pass 下降");
            Assert.Greater(after.a, 0.95f, "烘干不得削减覆盖率（A 通道）");
            Assert.Greater(after.r, 0.85f, "烘干不得改动队伍归属（R 通道）");
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
