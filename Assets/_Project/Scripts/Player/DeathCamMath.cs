using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// 阵亡观战镜头的纯数学工具（对标 INKWAVE <c>cameraRig._spectate</c>，不依赖 MonoBehaviour，EditMode 可断言）：
    /// 1. 前 <see cref="HoldSeconds"/> 秒把视线停在阵亡点（看击杀爆墨），之后转向击杀者；
    /// 2. 机位站在「击杀者 → 阵亡点」连线的阵亡点一侧，半径 4.2m → 5.5m、高 2.4m，并缓慢绕击杀者漂移；
    /// 3. 观战 FOV 比正常第三人称收窄 <see cref="FovZoomDegrees"/>，把注意力压到击杀者身上。
    ///
    /// 只负责几何取值；遮挡夹紧（球探针）、地面保护与平滑由 <see cref="TopDownCameraFollow"/> 处理。
    /// </summary>
    public static class DeathCamMath
    {
        /// <summary>死亡后先停在阵亡点的时间（秒），与 INKWAVE 一致。</summary>
        public const float HoldSeconds = 0.8f;

        /// <summary>停留段的机位半径（米）。</summary>
        public const float StartRadius = 4.2f;

        /// <summary>转向击杀者之后的机位半径（米）。</summary>
        public const float Radius = 5.5f;

        /// <summary>机位相对视线焦点的高度（米）。</summary>
        public const float Height = 2.4f;

        /// <summary>视线焦点相对单位脚下锚点的高度（米，胸口偏上）。</summary>
        public const float LookHeight = 1.1f;

        /// <summary>绕击杀者的漂移角速度（弧度/秒）。</summary>
        public const float DriftRadiansPerSecond = 0.07f;

        /// <summary>漂移封顶时间（秒）：观战久了不再继续转圈。</summary>
        public const float DriftClampSeconds = 6f;

        /// <summary>观战 FOV 相对正常第三人称的收窄量（度）。</summary>
        public const float FovZoomDegrees = 6f;

        /// <summary>停留段视线焦点平滑速率（1/s）。</summary>
        public const float HoldLookSmoothRate = 7f;

        /// <summary>转向击杀者后视线焦点平滑速率（1/s）。</summary>
        public const float LookSmoothRate = 4.5f;

        /// <summary>机位平滑速率（1/s）。</summary>
        public const float PositionSmoothRate = 3f;

        /// <summary>
        /// 当前视线焦点（世界坐标）。停留段或无击杀者（环境死亡）时看阵亡点；
        /// 之后看击杀者最后已知位置（击杀者中途阵亡由调用方冻结位置，不把镜头拉回自己尸体）。
        /// </summary>
        public static Vector3 ResolveFocusPoint(
            float elapsedSeconds,
            Vector3 victimPosition,
            Vector3 killerPosition,
            bool hasKiller)
        {
            Vector3 anchor = elapsedSeconds < HoldSeconds || !hasKiller ? victimPosition : killerPosition;
            return anchor + Vector3.up * LookHeight;
        }

        /// <summary>
        /// 当前机位（世界坐标）。水平方向取「焦点 → 阵亡点」的反向，即始终站在阵亡点这一侧的
        /// 击杀者周围；方向退化（焦点正好在阵亡点上方）时用 <paramref name="fallbackDirection"/>
        /// 保持死亡瞬间的机位不改朝向，避免开局凭空甩 90°。
        /// </summary>
        public static Vector3 ResolveCameraPosition(
            float elapsedSeconds,
            Vector3 victimPosition,
            Vector3 focusPoint,
            Vector3 fallbackDirection)
        {
            Vector3 away = victimPosition - focusPoint;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f)
            {
                away = fallbackDirection;
                away.y = 0f;
            }
            if (away.sqrMagnitude < 0.0001f)
            {
                away = Vector3.forward;
            }
            away.Normalize();

            float drift = Mathf.Min(Mathf.Max(0f, elapsedSeconds), DriftClampSeconds) * DriftRadiansPerSecond;
            float angle = Mathf.Atan2(away.x, away.z) + drift;
            float radius = elapsedSeconds < HoldSeconds ? StartRadius : Radius;
            return focusPoint + new Vector3(Mathf.Sin(angle) * radius, Height, Mathf.Cos(angle) * radius);
        }

        /// <summary>观战期间的目标 FOV（度），与正常第三人称同一套 25~90 钳制。</summary>
        public static float ResolveFieldOfView(float baseFov)
        {
            return Mathf.Clamp(baseFov - FovZoomDegrees, 25f, 90f);
        }

        /// <summary>视线焦点平滑速率：停留段更快咬住阵亡点，转向击杀者后放慢（对齐 INKWAVE 7 / 4.5）。</summary>
        public static float ResolveLookSmoothRate(float elapsedSeconds)
        {
            return elapsedSeconds < HoldSeconds ? HoldLookSmoothRate : LookSmoothRate;
        }

        /// <summary>指数平滑系数（帧率无关）：1 - e^(-rate·dt)。</summary>
        public static float ExpSmoothFactor(float ratePerSecond, float deltaSeconds)
        {
            return 1f - Mathf.Exp(-Mathf.Max(0f, ratePerSecond) * Mathf.Max(0f, deltaSeconds));
        }
    }
}
