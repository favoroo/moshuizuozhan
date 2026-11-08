using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// 开赛扫场镜头的输入数据（P41）：由 <see cref="TopDownCameraFollow"/> 在进入 Countdown 时按
    /// 当前玩家出生位姿 + 场地尺寸算好，再交给 <see cref="MatchIntroCameraMath"/> 求值。
    ///
    /// 只放几何参数，不含任何 MonoBehaviour 引用，便于 EditMode 直接构造断言。
    /// </summary>
    public struct MatchIntroCameraSetup
    {
        /// <summary>场地中心（水平面，y 通常为 0）。</summary>
        public Vector3 ArenaCenter;

        /// <summary>玩家脚底出生位置。</summary>
        public Vector3 PlayerFeet;

        /// <summary>玩家朝向（度）。决定「敌方半场」在哪个方向，扫场机位永远架在敌方那侧。</summary>
        public float PlayerYawDegrees;

        /// <summary>扫场结束时必须精确落到的跟随机位（= SnapImmediately 的解）。</summary>
        public Vector3 FinalCameraPosition;

        /// <summary>扫场结束时的注视点（= 跟随机位视轴前方 10m）。</summary>
        public Vector3 FinalLookTarget;

        /// <summary>跟随机位的基准 FOV（度）。</summary>
        public float BaseFov;

        /// <summary>高空扫场段的 FOV（度）：比基准更广，读作「俯瞰整块场地」。</summary>
        public float OverviewFov;

        /// <summary>高空扫场段的机位高度（米）。</summary>
        public float OverviewHeight;

        /// <summary>高空机位从场地中心朝敌方半场后退的距离（米）。</summary>
        public float OverviewBackDistance;

        /// <summary>高空机位相对场地的横向偏移（米），让开场构图是斜角而非正对。</summary>
        public float OverviewSideOffset;

        /// <summary>扫场段绕场地中心横向划过的角度（度）。</summary>
        public float SweepDegrees;

        /// <summary>扫场段占整条路径的比例（0~1）。之后是俯冲段。</summary>
        public float DiveStart01;

        /// <summary>俯冲段的贝塞尔抬升量（米）：让镜头从上方掠过来，而不是贴着地面穿过去。</summary>
        public float ArcLift;
    }

    /// <summary>某一时刻的镜头姿态。</summary>
    public struct MatchIntroCameraPose
    {
        public Vector3 Position;
        public Vector3 LookTarget;
        public float Fov;
    }

    /// <summary>
    /// 开赛扫场镜头的纯数学求值（P41，无 GameObject 依赖，EditMode 可断言）。
    ///
    /// 路径分两段，对齐 INKWAVE `_intro()` 的「高空俯冲到玩家身后」：
    /// 1. 扫场段（t ∈ [0, DiveStart01]）：机位架在敌方半场高空，绕场地中心横向划过
    ///    <see cref="MatchIntroCameraSetup.SweepDegrees"/>，同时缓慢下降 18% 高度，注视点保持在场地中心；
    /// 2. 俯冲段（t ∈ [DiveStart01, 1]）：二次贝塞尔从扫场终点收到跟随机位，控制点在跟随机位
    ///    「上方 + 后方」，保证镜头是掠过来而不是穿场地过来；注视点同步滑到玩家视轴前方。
    ///
    /// 硬约束：t = 1 时必须精确等于 <see cref="MatchIntroCameraSetup.FinalCameraPosition"/> /
    /// <see cref="MatchIntroCameraSetup.FinalLookTarget"/> / <see cref="MatchIntroCameraSetup.BaseFov"/>，
    /// 这样切回跟随相机的瞬间没有任何跳变。
    /// </summary>
    public static class MatchIntroCameraMath
    {
        public const float DefaultOverviewHeight = 25f;
        public const float DefaultOverviewBackDistance = 19f;
        public const float DefaultOverviewSideOffset = 9f;
        public const float DefaultSweepDegrees = 34f;
        public const float DefaultOverviewFovBonus = 12f;
        public const float DefaultDiveStart01 = 0.5f;
        public const float DefaultArcLift = 7f;

        /// <summary>扫场段结束时机位下降的比例（25m → 20.5m）。</summary>
        private const float OverviewHeightFalloff = 0.18f;

        /// <summary>扫场段注视点朝玩家半场偏移的比例，给俯冲一个方向感。</summary>
        private const float OverviewLookBias = 0.25f;

        public static float SmoothStep(float t)
        {
            float c = Mathf.Clamp01(t);
            return c * c * (3f - 2f * c);
        }

        /// <summary>求某一时刻（0~1）的镜头姿态。NaN / 越界一律安全钳制。</summary>
        public static MatchIntroCameraPose Evaluate(float t01, in MatchIntroCameraSetup setup)
        {
            float t = float.IsNaN(t01) ? 1f : Mathf.Clamp01(t01);
            float diveStart = float.IsNaN(setup.DiveStart01)
                ? DefaultDiveStart01
                : Mathf.Clamp(setup.DiveStart01, 0.05f, 0.95f);

            Vector3 overviewLook = GetOverviewLook(setup);
            Vector3 diveLookStart = Vector3.Lerp(overviewLook, GetPlayerLookBias(setup), OverviewLookBias);

            if (t <= diveStart)
            {
                float k = SmoothStep(t / diveStart);
                return new MatchIntroCameraPose
                {
                    Position = EvaluateOverview(k, setup),
                    LookTarget = Vector3.Lerp(overviewLook, diveLookStart, k),
                    Fov = setup.OverviewFov
                };
            }

            float d = SmoothStep((t - diveStart) / (1f - diveStart));
            Vector3 from = EvaluateOverview(1f, setup);
            Vector3 control = BuildDiveControl(setup);
            return new MatchIntroCameraPose
            {
                Position = QuadraticBezier(from, control, setup.FinalCameraPosition, d),
                LookTarget = Vector3.Lerp(diveLookStart, setup.FinalLookTarget, d),
                Fov = Mathf.Lerp(setup.OverviewFov, setup.BaseFov, d)
            };
        }

        /// <summary>
        /// 扫场段的机位：以玩家朝向为「前」，机位落在敌方半场（前）× 后退距离 + 右 × 横向偏移，
        /// 再绕场地中心按 <see cref="MatchIntroCameraSetup.SweepDegrees"/> 横向划过。
        /// </summary>
        public static Vector3 EvaluateOverview(float k, in MatchIntroCameraSetup setup)
        {
            float kk = float.IsNaN(k) ? 1f : Mathf.Clamp01(k);
            Quaternion yawRot = Quaternion.Euler(0f, setup.PlayerYawDegrees, 0f);
            Vector3 forward = yawRot * Vector3.forward;
            Vector3 right = yawRot * Vector3.right;

            Vector3 offset = forward * setup.OverviewBackDistance + right * setup.OverviewSideOffset;
            offset = Quaternion.Euler(0f, setup.SweepDegrees * kk, 0f) * offset;

            float height = setup.OverviewHeight * (1f - OverviewHeightFalloff * kk);
            return setup.ArenaCenter + offset + Vector3.up * height;
        }

        /// <summary>扫场段的注视点：场地中心略抬高，避免机位正对地板。</summary>
        public static Vector3 GetOverviewLook(in MatchIntroCameraSetup setup)
        {
            return setup.ArenaCenter + Vector3.up * 1.5f;
        }

        /// <summary>俯冲段控制点：跟随机位的正上方 + 后上方，保证镜头是掠下来的。</summary>
        public static Vector3 BuildDiveControl(in MatchIntroCameraSetup setup)
        {
            float lift = Mathf.Max(0f, setup.ArcLift);
            Vector3 behind = setup.FinalCameraPosition - setup.FinalLookTarget;
            behind.y = 0f;
            if (behind.sqrMagnitude < 0.0001f)
                behind = Vector3.zero;
            else
                behind.Normalize();

            return setup.FinalCameraPosition + Vector3.up * lift + behind * (lift * 0.55f);
        }

        public static Vector3 QuadraticBezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float k = float.IsNaN(t) ? 1f : Mathf.Clamp01(t);
            float inv = 1f - k;
            return inv * inv * a + 2f * inv * k * b + k * k * c;
        }

        /// <summary>两段姿态之间做混合（扫场被提前打断时收尾用）。</summary>
        public static MatchIntroCameraPose LerpPose(in MatchIntroCameraPose from, in MatchIntroCameraPose to, float t)
        {
            float k = float.IsNaN(t) ? 1f : Mathf.Clamp01(t);
            return new MatchIntroCameraPose
            {
                Position = Vector3.Lerp(from.Position, to.Position, k),
                LookTarget = Vector3.Lerp(from.LookTarget, to.LookTarget, k),
                Fov = Mathf.Lerp(from.Fov, to.Fov, k)
            };
        }

        private static Vector3 GetPlayerLookBias(in MatchIntroCameraSetup setup)
        {
            return setup.PlayerFeet + Vector3.up * 1.5f;
        }
    }
}
