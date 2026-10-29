namespace P1.Player
{
    /// <summary>
    /// 潜行/射击按键仲裁（纯逻辑，可 EditMode 测试）。
    /// 规则：最后一次按下的键决定当前形态。
    /// - 射击中按潜行键 → 立刻停火潜入；
    /// - 潜行中按开火 → 立刻出枪；
    /// - 只松开开火、仍按住潜行键 → 自动回到潜行；
    /// - 手雷/大招（可用时）按下会打断潜行；
    /// - 脚下没有己方墨水时潜行不生效，开火优先，避免按住 Shift 时按键失效。
    ///
    /// 丝滑进出水仲裁（带 deltaTime 的 Update 重载）：
    /// - 潜墨钻出人形的瞬间起 <see cref="EmergeTransitionTime"/>（默认 0.07s）快速过渡期内
    ///   主武器不开火（IsEmerging），但期间按下过的开火键进入 <see cref="FireBufferDuration"/>
    ///   （默认 0.16s）缓冲队列，过渡结束立刻补射 —— 绝不吞键；
    /// - 不带 deltaTime 的旧 6 参重载保持旧「当帧即时开火」语义，向后兼容。
    /// </summary>
    public sealed class SwimFormInputArbiter
    {
        private bool _swimHeld;
        private bool _fireHeld;
        private bool _wantsSwimForm;

        // 时间仲裁状态（仅 deltaTime 版 Update 驱动；旧版重载会清零以保持旧语义）
        private float _emergeTimer;
        private float _fireBufferTimer;
        private bool _wasSwimmingIntent;

        /// <summary>潜墨钻出人形后的快速过渡时长（秒），对齐 Inkwave PLAYER.emergeDelay。</summary>
        public float EmergeTransitionTime = 0.07f;

        /// <summary>过渡期内开火按键的缓冲时长（秒），对齐 Inkwave PLAYER.fireBuffer。</summary>
        public float FireBufferDuration = 0.16f;

        public bool SwimPressedThisFrame { get; private set; }
        public bool FirePressedThisFrame { get; private set; }
        public bool WantsSwim { get; private set; }
        public bool WantsFire { get; private set; }

        /// <summary>是否正处于出水快速过渡期（0.07s）：开火意图有效，但主武器暂扣扳机。</summary>
        public bool IsEmerging => _emergeTimer > 0f;

        /// <summary>过渡期剩余时间（秒）。</summary>
        public float EmergeRemaining => _emergeTimer;

        /// <summary>开火缓冲剩余时间（秒）：&gt;0 表示有一次尚未射出的按键等待补射。</summary>
        public float FireBufferRemaining => _fireBufferTimer;

        /// <summary>清空按键与形态记忆。复活、结算、输入被禁用时调用，避免恢复时误触发。</summary>
        public void Reset()
        {
            _swimHeld = false;
            _fireHeld = false;
            _wantsSwimForm = false;
            SwimPressedThisFrame = false;
            FirePressedThisFrame = false;
            WantsSwim = false;
            WantsFire = false;
            _emergeTimer = 0f;
            _fireBufferTimer = 0f;
            _wasSwimmingIntent = false;
        }

        /// <summary>旧版 6 参重载：不推进时间模型，保持「当帧即时开火」的向后兼容语义。</summary>
        public void Update(
            bool swimHeld,
            bool fireHeld,
            bool bombPressed,
            bool specialReadyPressed,
            bool swimAvailable,
            bool specialLocked)
        {
            UpdateIntent(swimHeld, fireHeld, bombPressed, specialReadyPressed, swimAvailable, specialLocked);
            _emergeTimer = 0f;
            _fireBufferTimer = 0f;
            _wasSwimmingIntent = WantsSwim;
        }

        /// <param name="swimHeld">潜行键是否按住（Shift / 右键 / 手柄左扳机 / 触控按钮）。</param>
        /// <param name="fireHeld">开火键是否按住（左键 / 右扳机 / 触控按钮）。</param>
        /// <param name="bombPressed">本帧是否按下了手雷。</param>
        /// <param name="specialReadyPressed">本帧是否按下了已就绪的大招（未就绪不打断潜行）。</param>
        /// <param name="swimAvailable">脚下是否具备潜行条件（己方墨水，或当前已在潜行）。</param>
        /// <param name="specialLocked">是否正在释放大招（锁定人形动作）。</param>
        /// <param name="deltaTime">本帧时长（秒）。驱动出水过渡计时与开火缓冲衰减。</param>
        public void Update(
            bool swimHeld,
            bool fireHeld,
            bool bombPressed,
            bool specialReadyPressed,
            bool swimAvailable,
            bool specialLocked,
            float deltaTime)
        {
            if (deltaTime < 0f) deltaTime = 0f;

            UpdateIntent(swimHeld, fireHeld, bombPressed, specialReadyPressed, swimAvailable, specialLocked);

            // 1. 潜墨 → 人形瞬间：启动快速过渡计时器（钻出开火的第一发被暂扣 0.07s，而非吞键）
            if (_wasSwimmingIntent && !WantsSwim)
            {
                _emergeTimer = EmergeTransitionTime;
            }
            else if (_emergeTimer > 0f)
            {
                _emergeTimer -= deltaTime;
                if (_emergeTimer < 0f) _emergeTimer = 0f;
            }
            _wasSwimmingIntent = WantsSwim;

            // 2. 开火缓冲：过渡期内按下过开火 → 记满 0.16s 缓冲；未按下则逐帧衰减
            if (FirePressedThisFrame)
            {
                _fireBufferTimer = FireBufferDuration;
            }
            else if (_fireBufferTimer > 0f)
            {
                _fireBufferTimer -= deltaTime;
                if (_fireBufferTimer < 0f) _fireBufferTimer = 0f;
            }

            // 3. 缓冲消费：过渡结束且开火键仍按住时，连续射击本身即覆盖缓冲，直接清空；
            //    「点一下就松手」的缓冲则由 TryConsumeFireBuffer 在过渡结束后补射一发。
            if (!IsEmerging && WantsFire)
            {
                _fireBufferTimer = 0f;
            }
        }

        /// <summary>
        /// 取出一次「缓冲期内按下但尚未射出」的开火输入。
        /// 出水过渡结束、开火键已松开且缓冲未过期时返回 true（并消费缓冲）—— 绝不吞键。
        /// </summary>
        public bool TryConsumeFireBuffer()
        {
            if (_fireBufferTimer > 0f && !IsEmerging && !WantsSwim && !WantsFire)
            {
                _fireBufferTimer = 0f;
                return true;
            }
            return false;
        }

        private void UpdateIntent(
            bool swimHeld,
            bool fireHeld,
            bool bombPressed,
            bool specialReadyPressed,
            bool swimAvailable,
            bool specialLocked)
        {
            SwimPressedThisFrame = swimHeld && !_swimHeld;
            FirePressedThisFrame = fireHeld && !_fireHeld;
            _swimHeld = swimHeld;
            _fireHeld = fireHeld;

            if (SwimPressedThisFrame)
            {
                _wantsSwimForm = true;
            }
            else if (FirePressedThisFrame || bombPressed || specialReadyPressed || !swimHeld)
            {
                _wantsSwimForm = false;
            }
            else if (!fireHeld)
            {
                // 松开了开火但还按着潜行键：回到潜行，支持“按住 Shift、点按开火”的连打节奏
                _wantsSwimForm = true;
            }

            WantsSwim = _wantsSwimForm && swimAvailable && !specialLocked;
            WantsFire = fireHeld && !WantsSwim;
        }
    }
}
