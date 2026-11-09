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

        [Tooltip("缎面余晖秒数：镜面期结束后墨面仍保留低幅环境反射的时长（P18）。" +
                 "应明显大于 inkDryingSeconds，否则战场上大部分墨会退化为哑光色块")]
        [Range(0f, 120f)]
        public float inkSheenSeconds = 22f;

        [Tooltip("边缘先干偏置（着色读数，不改图集数据）：0 = 关闭（整片均匀干燥），" +
                 "1 = 薄边缘（覆盖率低处，即墨迹外缘）显著更快失去湿润高光，读作墨迹从外缘向内收干")]
        [Range(0f, 1f)]
        public float inkEdgeDryBias = 0.5f;

        [Tooltip("单个墨斑铺开动画时长（秒）；0 = 立即出现。" +
                 "注意图集 G 通道时间戳只有 8bit（周期/256 ≈ 0.125s/级），动画时长接近或短于量化步长时" +
                 "时间戳误差会吃掉大半动画，读起来就是「落地后隔一下才看到墨」，所以保持 ≤0.1s")]
        [Range(0f, 0.5f)]
        public float splatGrowSeconds = 0.08f;

        [Tooltip("墨迹图集湿润时间戳回绕周期（秒）：G 通道存 frac(落墨时刻/周期)，8bit 精度 = 周期/256。"+
                 "默认 32s → 0.125s/级（湿润度量化步长约 0.02）；周期越大回绕伪影越少但时间戳越粗")]
        [Range(8f, 120f)]
        public float atlasWetClockPeriod = 32f;

        [Tooltip("生长动画片元侧侵蚀的最大回缩距离（米）：新墨斑以最终形态写入图集，可见边缘从最终轮廓" +
                 "向内回缩这么多再随时间松开。**按绝对米数回缩**，所以对小块墨斑杀伤极大：" +
                 "墨迹半径 0.44m（沿途滴墨）时 0.4m 回缩会让它落地瞬间几乎不可见，" +
                 "弹丸落点的有机形状（射线/飞溅小点）也要等 0.1s 后才长出来（读作「涂地有延迟」）。" +
                 "0.18m 下落地即见 65%~77% 尺寸，观感与 INKWAVE「主体从 40% 铺开」一致")]
        [Range(0f, 2f)]
        public float atlasGrowShrinkMeters = 0.18f;

        [Tooltip("每个墨斑的色调微差强度：0 = 完全纯色，1 = 参考实现默认")]
        [Range(0f, 2f)]
        public float splatToneStrength = 1f;

        [Tooltip("墨斑方向性拉伸的全局倍率：0 = 关闭（墨斑恒为圆盘+wobble），1 = 完全按 PaintCircle 传入的" +
                 " directionXZ/stretch01 逐墨斑拉伸（主体沿喷射方向拉长、射线向前偏置）。" +
                 "玩法网格判定不受影响，仍是正圆")]
        [Range(0f, 2f)]
        public float splatDirectionalStretch = 1f;

        [Tooltip("每帧最多绘制（含重绘）的墨斑数，超出者直接以完成态补画")]
        [Range(32, 384)]
        public int maxSplatsPerFrame = 192;

        [Tooltip("图集是否生成 mipmap。开启后远景墨迹不再摩尔纹/闪烁（4096² 图集铺 40×56m，" +
                 "一个纹素约 1.4cm，缩到远处严重 undersample）；mip 链受 atlasMipIntervalSeconds 节流，" +
                 "第三人称斜视地面时落点附近采样 LOD 在 1~3，新墨只写进 mip0，重建前最多延迟一个间隔。" +
                 "0.20s 是闪烁与「涂地延迟」的折中；想更跟手可降到 0.15s（重建尖刺更频繁）。")]
        public bool atlasMipmaps = true;

        [Tooltip("墨迹图集 mip 重建最小间隔（秒）：只在有新落墨时重建，间隔越大越省 GPU 尖刺。" +
                 "仅在 atlasMipmaps=true 时生效")]
        [Range(0.1f, 2f)]
        public float atlasMipIntervalSeconds = 0.2f;

        [Tooltip("墨迹图集向场地四周外扩的边距（米）：避免场地边界处的双线性过滤/写入被图集边缘截断。" +
                 "可见受墨范围仍是 40x56 计分场 —— 场外表面（场外码头、边界墙顶）不再显墨。0 = 默认 1.5m；计分网格本身不变")]
        [Range(0f, 4f)]
        public float atlasVisualMargin = 1.5f;

        [Header("掩体接触遮蔽（地面软化）")]
        [Tooltip("箱底/墙脚的软遮蔽强度：加载时用不可涂格烘一张 R8 遮蔽场（PC/移动端共用），" +
                 "只压地面基底、不压实时阴影与墨迹（墨面本来就有 shadowmap）。" +
                 "补的是实时阴影（shadowDistance 48m 外）和 SSAO（屏幕空间 0.6m）都覆盖不到的远景落地感。0 = 关闭")]
        [Range(0f, 1f)]
        public float groundContactAOStrength = 0.45f;

        [Tooltip("软遮蔽从掩体足迹向外扩散的半径（米）：越大越像环境遮蔽，越小越像接触线。" +
                 "按 0.1~1.5m 取，超过 2.5m 会读作大片阴天污渍")]
        [Range(0.15f, 4f)]
        public float groundContactAORadiusMeters = 1.2f;

        [Tooltip("小地图贴图刷新频率（Hz）；0 = 每帧刷新")]
        [Range(0f, 30f)]
        public float minimapRefreshHz = 12f;

        [Tooltip("地面波纹 MaterialPropertyBlock 同步频率（Hz）；0 = 每帧同步")]
        [Range(0f, 60f)]
        public float rippleSyncHz = 30f;

        [Tooltip("墙面墨迹贴图上传频率（Hz）；0 = 每帧上传")]
        [Range(0f, 60f)]
        public float wallFlushHz = 30f;

        [Header("墙面墨迹观感")]
        [Tooltip("墙面墨迹色调微差强度（着色器程序化 hash，0 = 纯色，与地面 splatToneStrength 呼应）")]
        [Range(0f, 2f)]
        public float wallToneJitter = 1f;

        [Tooltip("墙面湿润墨迹向下垂流的可见强度（0 = 关闭；湿墨下方拉出向下渐隐的细流）")]
        [Range(0f, 2f)]
        public float wallDripIntensity = 1f;

        [Tooltip("墙面墨迹软边强度：0 = 保留格间硬边，1 = 与地面同级的 fwidth 软边")]
        [Range(0f, 1f)]
        public float wallEdgeSoftness = 1f;

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

        [Tooltip("保留开关：当前未实现。覆盖率始终只统计 40x56 地面网格（墙面墨迹仅可视化 + 攀爬 + 大招充能），" +
                 "改这个值不会影响比分或结算")]
        public bool countWallInCoverage = false;

        /// <summary>
        /// 单张墙墨格面积（m²）。墙面充能/统计必须用它换算，不能借用地面格
        /// <see cref="InkSurfaceController.CellArea"/>：墙格边长 0.1m，地面格在 400 分辨率下是 0.1x0.14m，
        /// 混用会把墙墨面积多算约 1.4 倍。
        /// </summary>
        public float WallCellArea
        {
            get
            {
                float cell = Mathf.Max(0.01f, wallCellSize);
                return cell * cell;
            }
        }

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
