using UnityEngine;

namespace P1.Weapons
{
    /// <summary>
    /// 重力弹道反解：给定枪口、目标点、初速与重力，求出“低伸弹道”的发射方向。
    /// 解算基于恒定重力解析式，而 <see cref="InkProjectilePool"/> 使用梯形积分推进，
    /// 两者在数学上完全等价，因此有效射程内落点与瞄准点严格重合。
    /// 目标不可达（朝天、超出初速）时沿「枪口 → 目标」直接发射并钳制仰角，绝不退回水平平射。
    /// </summary>
    public static class InkBallistics
    {
        /// <summary>仰角上限（度）。超过则视为不可达，沿直接瞄准方向钳制到该角度。</summary>
        public const float MaxLaunchPitchDegrees = 72f;

        /// <summary>水平距离小于该值时不做反解，直接指向目标（避免近距离除零与病态解）。</summary>
        public const float MinSolveDistance = 0.35f;

        /// <summary>抛物武器（泼桶）最低飞行速度，避免近距离把速度解到 0。</summary>
        public const float MinLobSpeed = 5.5f;

        /// <summary>抛物武器最高仰角（弧度，约 69°），对齐 INKWAVE 的 clamp 上限。</summary>
        public const float MaxLobPitchRadians = 1.2f;

        /// <summary>
        /// 反解发射方向（单位向量）。目标可高于或低于枪口；超出 maxRange 时按最大射程截断。
        /// 判别式小于 0（初速不足以到达）或仰角超过上限时，沿「枪口 → 目标」直接发射并把俯仰钳制到
        /// <see cref="MaxLaunchPitchDegrees"/>，因此朝天瞄准会真的朝天上打。
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

            // 直接瞄准方向：不可达时沿它发射（含俯仰），这是“抬头能打天上”的关键。
            Vector3 direct = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : Vector3.forward;

            if (maxRange > 0f && horizontalDistance > maxRange)
            {
                flat *= maxRange / horizontalDistance;
                horizontalDistance = maxRange;
            }

            if (speed <= 0.01f || gravity <= 0.0001f || horizontalDistance < MinSolveDistance)
                return ClampPitch(direct, MaxLaunchPitchDegrees);

            float verticalDelta = targetPosition.y - muzzlePosition.y;
            float speedSq = speed * speed;
            float discriminant = speedSq * speedSq
                - gravity * (gravity * horizontalDistance * horizontalDistance + 2f * verticalDelta * speedSq);
            if (discriminant < 0f)
                return ClampPitch(direct, MaxLaunchPitchDegrees);

            // 取低伸解：同等距离下飞行时间更短、弹道更平，符合喷射枪的手感预期。
            float tanPitch = (speedSq - Mathf.Sqrt(discriminant)) / (gravity * horizontalDistance);
            float pitch = Mathf.Atan(tanPitch);
            if (Mathf.Abs(pitch) > MaxLaunchPitchDegrees * Mathf.Deg2Rad)
                return ClampPitch(direct, MaxLaunchPitchDegrees);

            Vector3 horizontalDir = flat / horizontalDistance;
            Vector3 direction = horizontalDir * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch);
            return direction.normalized;
        }

        /// <summary>
        /// 绕弹道轴的锥形散布（对齐 INKWAVE 的 <c>_spread</c>）：水平方向满幅、垂直方向压到 55%，
        /// 因此即使朝天上打，散布也是以准星为中心的一圈，而不是被压成一条线。
        /// </summary>
        public static Vector3 ApplySpread(Vector3 direction, float degrees)
        {
            Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            if (degrees <= 0.0001f)
                return dir;

            float radius = degrees * Mathf.Deg2Rad * Mathf.Sqrt(Random.value);
            float angle = Random.value * Mathf.PI * 2f;

            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 1e-4f)
                side = Vector3.right;
            side.Normalize();
            Vector3 up = Vector3.Cross(dir, side);

            float tan = Mathf.Tan(radius);
            Vector3 spread = dir
                + side * (Mathf.Cos(angle) * tan)
                + up * (Mathf.Sin(angle) * tan * 0.55f);
            return spread.normalized;
        }

        /// <summary>
        /// 抛物武器（泼桶）的仰角与初速解算，移植自 INKWAVE 的 <c>fireSlosh</c>：
        /// 近距离固定 <paramref name="basePitchRadians"/>（默认 18°）抬升、由初速控制落点；
        /// 超出该角度满速可达的距离（远目标或高点）时改成低伸弹道解，仰角钳制到 [base, 69°]，
        /// 保证头滴始终落在准星上，抬头时能越过掩体泼到高台。
        /// </summary>
        public static void SolveLobLaunch(
            Vector3 muzzlePosition,
            Vector3 targetPosition,
            float speed,
            float gravity,
            float maxRange,
            float basePitchRadians,
            out float pitchRadians,
            out float launchSpeed)
        {
            float basePitch = Mathf.Clamp(basePitchRadians, 0.01f, MaxLobPitchRadians);
            float g = Mathf.Max(0.01f, gravity);
            float v0 = Mathf.Max(0.1f, speed);

            Vector3 toTarget = targetPosition - muzzlePosition;
            float range = Mathf.Max(1.2f, maxRange);
            float horizontal = Mathf.Clamp(new Vector2(toTarget.x, toTarget.z).magnitude, 1.2f, range);
            float vertical = Mathf.Clamp(toTarget.y, -4f, 5f);

            float cosBase = Mathf.Cos(basePitch);
            float denominator = 2f * cosBase * cosBase * (horizontal * Mathf.Tan(basePitch) - vertical);
            float requiredSpeed = denominator > 1e-3f
                ? Mathf.Sqrt(g * horizontal * horizontal / denominator)
                : float.PositiveInfinity;

            if (requiredSpeed <= v0)
            {
                // 固定仰角就能落在准星上：只调初速，弹道始终是同一个抛桶手感。
                launchSpeed = Mathf.Max(MinLobSpeed, requiredSpeed);
                pitchRadians = basePitch;
                return;
            }

            // 满速 18° 也够不到（远目标 / 高台 / 朝天）：改用低伸弹道解抬高仰角。
            float v2 = v0 * v0;
            float discriminant = v2 * v2 - g * (g * horizontal * horizontal + 2f * vertical * v2);
            float pitch = discriminant >= 0f
                ? Mathf.Atan((v2 - Mathf.Sqrt(discriminant)) / (g * horizontal))
                : Mathf.PI * 0.25f;

            pitchRadians = Mathf.Clamp(pitch, basePitch, MaxLobPitchRadians);
            launchSpeed = v0;
        }

        /// <summary>把方向钳制到 ±maxPitchDegrees 的俯仰范围内；接近垂直时退回纯上下方向。</summary>
        private static Vector3 ClampPitch(Vector3 direction, float maxPitchDegrees)
        {
            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            float horizontal = flat.magnitude;
            if (horizontal < 0.0001f)
                return direction.y >= 0f ? Vector3.up : Vector3.down;

            float maxRadians = maxPitchDegrees * Mathf.Deg2Rad;
            float pitch = Mathf.Clamp(Mathf.Atan2(direction.y, horizontal), -maxRadians, maxRadians);
            Vector3 horizontalDir = flat / horizontal;
            Vector3 clamped = horizontalDir * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch);
            return clamped.normalized;
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
