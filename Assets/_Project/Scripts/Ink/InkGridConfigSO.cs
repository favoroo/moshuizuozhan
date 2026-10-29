using P1.Core;
using UnityEngine;

namespace P1.Ink
{
    [CreateAssetMenu(fileName = "InkGridConfig", menuName = "P1/Ink Grid Config")]
    public class InkGridConfigSO : ScriptableObject
    {
        [Header("场地尺寸与网格分辨率")]
        [Tooltip("场地世界尺寸（米），默认 40x40")]
        public Vector2 mapSize = new Vector2(40f, 40f);

        [Tooltip("桌面端贴图与网格边长（40m / 0.1m = 400）")]
        [Range(64, 512)]
        public int desktopResolution = 400;

        [Tooltip("移动端贴图与网格边长（40m / 0.2m = 200）")]
        [Range(64, 512)]
        public int mobileResolution = 200;

        [Header("墨迹图集（纯视觉层，决定油漆边缘精细度）")]
        [Tooltip("桌面端墨迹图集分辨率：40m 场地下 4096 = 1cm/texel，边缘才能是平滑曲线而非 10cm 方格阶梯")]
        [Range(512, 4096)]
        public int inkAtlasResolutionDesktop = 4096;

        [Tooltip("移动端墨迹图集分辨率：显存与填充率受限，1024 = 3.9cm/texel")]
        [Range(512, 2048)]
        public int inkAtlasResolutionMobile = 1024;

        [Tooltip("墨水从湿润变干所需秒数（影响镜面高光与边缘厚度）")]
        [Range(0.5f, 30f)]
        public float inkDryingSeconds = 6f;

        [Tooltip("单个墨斑铺开动画时长（秒）；0 = 立即出现")]
        [Range(0f, 0.5f)]
        public float splatGrowSeconds = 0.12f;

        [Tooltip("每个墨斑的色调微差强度：0 = 完全纯色，1 = 参考实现默认")]
        [Range(0f, 2f)]
        public float splatToneStrength = 1f;

        [Tooltip("每帧最多绘制（含重绘）的墨斑数，超出者直接以完成态补画")]
        [Range(32, 384)]
        public int maxSplatsPerFrame = 192;

        [Tooltip("图集是否生成 mipmap（远景不闪烁，代价是每 0.25s 一次 mip 重建）")]
        public bool atlasMipmaps = true;

        [Tooltip("小地图贴图刷新频率（Hz）；0 = 每帧刷新")]
        [Range(0f, 30f)]
        public float minimapRefreshHz = 12f;

        [Header("队伍与地面配色")]
        public Color neutralGroundColor = new Color(0.22f, 0.24f, 0.28f, 0.0f);
        public Color teamAColor = new Color(1.00f, 0.44f, 0.06f, 1.0f);
        public Color teamBColor = new Color(0.15f, 0.42f, 1.00f, 1.0f);
        public Color blockedMapColor = new Color(0.10f, 0.11f, 0.14f, 1.0f);

        [Header("墙面涂地与攀爬")]
        [Tooltip("立面墙墨格边长（米）：越小越细腻但更吃内存。0.1m 时 1.8m 高掩体墙每面约 18x28 格")]
        [Range(0.04f, 0.5f)]
        public float wallCellSize = 0.1f;

        [Tooltip("关闭后墙面不可涂、也不可潜行上爬（回到纯地面涂地）")]
        public bool enableWallInk = true;

        [Tooltip("墙面墨水是否计入涂地覆盖率（参考 Splatoon：通常不计，仅地面决定胜负）")]
        public bool countWallInCoverage = false;

        public int GetPlatformResolution()
        {
            return Application.isMobilePlatform ? mobileResolution : desktopResolution;
        }

        public int GetPlatformAtlasResolution()
        {
            return Application.isMobilePlatform ? inkAtlasResolutionMobile : inkAtlasResolutionDesktop;
        }

        public Color GetTeamColor(Team team)
        {
            return team switch
            {
                Team.TeamA => teamAColor,
                Team.TeamB => teamBColor,
                _ => neutralGroundColor
            };
        }
    }
}
