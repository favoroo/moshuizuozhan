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
