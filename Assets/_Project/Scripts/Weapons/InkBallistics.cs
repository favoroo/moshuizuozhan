using UnityEngine;

namespace P1.Weapons
{
    /// <summary>
    /// 重力弹道反解：给定枪口、目标点、初速与重力，求出“低伸弹道”的发射方向。
    /// 解算基于恒定重力解析式，而 <see cref="InkProjectilePool"/> 使用梯形积分推进，
    /// 两者在数学上完全等价，因此有效射程内落点与瞄准点严格重合。
    /// </summary>
    public static class InkBallistics
    {
        /// <summary>仰角上限（度）。超过则视为不可达，退回直接指向目标。</summary>
        public const float MaxLaunchPitchDegrees = 72f;

        /// <summary>水平距离小于该值时不做反解，直接指向目标（避免近距离除零与病态解）。</summary>
        public const float MinSolveDistance = 0.35f;

        /// <summary>
        /// 反解发射方向（单位向量）。目标可高于或低于枪口；超出 maxRange 时按最大射程截断。
        /// 判别式小于 0（初速不足以到达）时返回直接指向目标的方向，保证调用方永远拿到合法向量。
        /// </summary>
        public static Vector3 SolveLaunchDirection(
            Vector3 muzzlePosition,
            Vector3 targetPosition,
            float speed,
            float gravity,
            float maxRange)
        {
            Vector3 toTarget = targetPosition - muzzlePosition;
            Vector3 flat = new Vector3(toTarget.x, 0f, toTarget.z);
            float horizontalDistance = flat.magnitude;

            if (maxRange > 0f && horizontalDistance > maxRange)
            {
                flat *= maxRange / horizontalDistance;
                horizontalDistance = maxRange;
            }

            Vector3 fallback;
            if (horizontalDistance > 0.0001f)
                fallback = flat / horizontalDistance;
            else if (toTarget.sqrMagnitude > 0.0001f)
                fallback = toTarget.normalized;
            else
                fallback = Vector3.forward;

            if (speed <= 0.01f || gravity <= 0.0001f || horizontalDistance < MinSolveDistance)
                return fallback;

            float verticalDelta = targetPosition.y - muzzlePosition.y;
            float speedSq = speed * speed;
            float discriminant = speedSq * speedSq
                - gravity * (gravity * horizontalDistance * horizontalDistance + 2f * verticalDelta * speedSq);
            if (discriminant < 0f)
                return fallback;

            // 取低伸解：同等距离下飞行时间更短、弹道更平，符合喷射枪的手感预期。
            float tanPitch = (speedSq - Mathf.Sqrt(discriminant)) / (gravity * horizontalDistance);
            float pitch = Mathf.Atan(tanPitch);
            if (Mathf.Abs(pitch) > MaxLaunchPitchDegrees * Mathf.Deg2Rad)
                return fallback;

            Vector3 horizontalDir = flat / horizontalDistance;
            Vector3 direction = horizontalDir * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch);
            return direction.normalized;
        }

        /// <summary>水平飞行时间（秒）：horizontalDistance / 水平分速度。</summary>
        public static float PredictFlightTime(Vector3 launchDirection, float speed, float horizontalDistance)
        {
            float horizontalSpeed = new Vector2(launchDirection.x, launchDirection.z).magnitude * speed;
            if (horizontalSpeed < 0.0001f)
                return 0f;
            return horizontalDistance / horizontalSpeed;
        }

        /// <summary>
        /// 梯形积分下的精确位置：与池内 <c>v1 = v0 + a·dt</c>、<c>x1 = x0 + (v0+v1)/2·dt</c> 逐帧推进一致。
        /// </summary>
        public static Vector3 PredictPosition(Vector3 origin, Vector3 initialVelocity, float gravity, float time)
        {
            Vector3 endVelocity = initialVelocity;
            endVelocity.y -= gravity * time;
            return origin + (initialVelocity + endVelocity) * 0.5f * time;
        }
    }
}
