using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// 物理阻尼弹簧参数：固有频率 + 阻尼比。相比固定时长的补间，物理弹簧可以
    /// 在动画中途被重新设为目标而不跳帧，非常适合按钮按下、卡片弹入这类可被打断的 UI。
    /// </summary>
    [System.Serializable]
    public struct UISpringSettings
    {
        [Tooltip("固有频率（Hz）：越大越快、越收紧")]
        [Range(0.25f, 20f)]
        public float frequency;

        [Tooltip("阻尼比：< 1 会过冲回弹，1 为临界阻尼，> 1 会迟滞")]
        [Range(0.05f, 1.5f)]
        public float damping;

        [Tooltip("单帧最大积分子步；低帧率下用子步保持稳定")]
        [Range(1, 16)]
        public int maxSubSteps;

        public UISpringSettings(float frequency, float damping, int maxSubSteps = 8)
        {
            this.frequency = frequency;
            this.damping = damping;
            this.maxSubSteps = maxSubSteps;
        }

        /// <summary>果冻弹入：约 12% 过冲，0.3s 左右收敛（对应 CSS spring 的观感）。</summary>
        public static UISpringSettings Pop => new UISpringSettings(3.8f, 0.55f, 10);

        /// <summary>按钮按下 / 松开：高频、小过冲，手感干脆。</summary>
        public static UISpringSettings Press => new UISpringSettings(7.5f, 0.78f, 8);

        /// <summary>数值/进度条追随：几乎不过冲，平滑贴合目标。</summary>
        public static UISpringSettings Follow => new UISpringSettings(8.5f, 0.9f, 8);

        /// <summary>临界阻尼收尾：无回弹地精确停住。</summary>
        public static UISpringSettings Settle => new UISpringSettings(5.5f, 1f, 8);

        public float FrequencyHz => frequency > 0.01f ? frequency : 3.8f;

        public float DampingRatio => damping > 0.01f ? damping : 0.55f;

        public float AngularFrequency => 2f * Mathf.PI * FrequencyHz;

        public int SubSteps => Mathf.Clamp(maxSubSteps, 1, 16);

        /// <summary>欠阻尼弹簧的理论过冲比例（0.163 即 16.3%）。</summary>
        public float Overshoot
        {
            get
            {
                float z = DampingRatio;
                if (z >= 1f) return 0f;

                return Mathf.Exp(-Mathf.PI * z / Mathf.Sqrt(1f - z * z));
            }
        }

        /// <summary>2% 误差带的近似收敛时间（秒），仅用于调参参考。</summary>
        public float SettleTime
        {
            get
            {
                float z = Mathf.Max(0.01f, DampingRatio);
                return 4f / (z * AngularFrequency);
            }
        }
    }

    /// <summary>标量弹簧。适合平滑百分比、液面、透明度等 0..1 数值。</summary>
    public struct UISpringFloat
    {
        public float Value;
        public float Velocity;

        public void Set(float value)
        {
            Value = value;
            Velocity = 0f;
        }

        /// <summary>推进一步；返回 true 表示已进入静止阈值并吸附到目标。</summary>
        public bool Step(float target, UISpringSettings settings, float deltaTime,
            float restDistance = 5e-4f, float restSpeed = 5e-3f)
        {
            if (deltaTime <= 0f) return false;

            float omega = settings.AngularFrequency;
            float stiffness = omega * omega;
            float drag = 2f * settings.DampingRatio * omega;

            // 半隐式欧拉在 ω·h 接近 2 时发散。这里按 ω·h ≤ 0.5 选子步，
            // 并在帧率骤降（如进入 Play 的第一帧）时宁可少积分一点，也绝不让弹簧爆炸。
            float maxStep = 0.5f / omega;
            int steps = Mathf.Clamp(Mathf.CeilToInt(deltaTime / maxStep), 1, settings.SubSteps);
            float h = Mathf.Min(deltaTime / steps, maxStep);

            for (int i = 0; i < steps; i++)
            {
                float acceleration = (target - Value) * stiffness - Velocity * drag;
                Velocity += acceleration * h;
                Value += Velocity * h;
            }

            if (Mathf.Abs(target - Value) <= restDistance && Mathf.Abs(Velocity) <= restSpeed)
            {
                Value = target;
                Velocity = 0f;
                return true;
            }

            return false;
        }
    }

    /// <summary>二维弹簧。适合锚点位移、进度条填充长度等。</summary>
    public struct UISpringVector2
    {
        public Vector2 Value;
        public Vector2 Velocity;

        public void Set(Vector2 value)
        {
            Value = value;
            Velocity = Vector2.zero;
        }

        public bool Step(Vector2 target, UISpringSettings settings, float deltaTime,
            float restDistance = 5e-4f, float restSpeed = 5e-3f)
        {
            if (deltaTime <= 0f) return false;

            float omega = settings.AngularFrequency;
            float stiffness = omega * omega;
            float drag = 2f * settings.DampingRatio * omega;

            // 半隐式欧拉在 ω·h 接近 2 时发散。这里按 ω·h ≤ 0.5 选子步，
            // 并在帧率骤降（如进入 Play 的第一帧）时宁可少积分一点，也绝不让弹簧爆炸。
            float maxStep = 0.5f / omega;
            int steps = Mathf.Clamp(Mathf.CeilToInt(deltaTime / maxStep), 1, settings.SubSteps);
            float h = Mathf.Min(deltaTime / steps, maxStep);

            for (int i = 0; i < steps; i++)
            {
                Vector2 acceleration = (target - Value) * stiffness - Velocity * drag;
                Velocity += acceleration * h;
                Value += Velocity * h;
            }

            if ((target - Value).sqrMagnitude <= restDistance * restDistance &&
                Velocity.sqrMagnitude <= restSpeed * restSpeed)
            {
                Value = target;
                Velocity = Vector2.zero;
                return true;
            }

            return false;
        }
    }

    /// <summary>三维弹簧。UISpringMotion 用它驱动局部缩放。</summary>
    public struct UISpringVector3
    {
        public Vector3 Value;
        public Vector3 Velocity;

        public void Set(Vector3 value)
        {
            Value = value;
            Velocity = Vector3.zero;
        }

        public bool Step(Vector3 target, UISpringSettings settings, float deltaTime,
            float restDistance = 5e-4f, float restSpeed = 5e-3f)
        {
            if (deltaTime <= 0f) return false;

            float omega = settings.AngularFrequency;
            float stiffness = omega * omega;
            float drag = 2f * settings.DampingRatio * omega;

            // 半隐式欧拉在 ω·h 接近 2 时发散。这里按 ω·h ≤ 0.5 选子步，
            // 并在帧率骤降（如进入 Play 的第一帧）时宁可少积分一点，也绝不让弹簧爆炸。
            float maxStep = 0.5f / omega;
            int steps = Mathf.Clamp(Mathf.CeilToInt(deltaTime / maxStep), 1, settings.SubSteps);
            float h = Mathf.Min(deltaTime / steps, maxStep);

            for (int i = 0; i < steps; i++)
            {
                Vector3 acceleration = (target - Value) * stiffness - Velocity * drag;
                Velocity += acceleration * h;
                Value += Velocity * h;
            }

            if ((target - Value).sqrMagnitude <= restDistance * restDistance &&
                Velocity.sqrMagnitude <= restSpeed * restSpeed)
            {
                Value = target;
                Velocity = Vector3.zero;
                return true;
            }

            return false;
        }
    }
}