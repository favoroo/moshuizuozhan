using System.Collections;
using System.Collections.Generic;
using P1.Weapons;
using UnityEngine;

namespace P1.Core
{
    /// <summary>循环音效通道分类（独立 AudioSource，支持淡入淡出与防叠音）。</summary>
    public enum LoopSfxKind
    {
        Roller,
        SplatlingSpin,
        Swim,
        Climb,
        EnemyInkSizzle,
        StormRain,
        ChargerCharge
    }

    /// <summary>UI 音效分类。</summary>
    public enum UiSoundKind
    {
        Click,
        Hover,
        Confirm,
        Toggle,
        Back,
        Slider,
        Error
    }

    /// <summary>
    /// 轻量音效管理器：管理 AudioSource 对象池，支持随机音高微调、高频音效节流、
    /// 比赛流程提示音通道（无语音）、双段动态 BGM（主阶段/决战段）、BGM Ducking 闪避以及独立循环音通道。
    /// 原有 SFX 与 BGM 公共接口保持向后兼容。
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("武器与涂地音效")]
        public AudioClip[] shotClips;
        public AudioClip[] inkHitGroundClips;
        public AudioClip[] hitEnemyClips;
        public AudioClip inkEmptyClip;
        public AudioClip inkRefillClip;

        [Header("武器操作与专属开火（P1-D）")]
        public AudioClip[] rollerFlickClips;
        public AudioClip chargerChargeClip;
        public AudioClip chargerFullClip;
        public AudioClip chargerShotClip;
        public AudioClip[] blasterShotClips;
        public AudioClip[] blasterBoomClips;
        public AudioClip sloshThrowClip;
        public AudioClip[] sloshLandClips;

        [Header("移动与机动（P2-A）")]
        public AudioClip squidInClip;
        public AudioClip squidOutClip;
        public AudioClip[] swimSplashClips;
        public AudioClip[] stepDryClips;
        public AudioClip[] stepInkBubbleClips;
        public AudioClip[] stepEnemyClips;
        public AudioClip jumpClip;
        public AudioClip landClip;
        public AudioClip dodgeRollClip;

        [Header("受击与战斗反馈（P6-A）")]
        public AudioClip hurtClip;
        public AudioClip[] hitMarkerClips;
        public AudioClip refillFullClip;

        [Header("副武器与大招（P3-B）")]
        public AudioClip bombThrowClip;
        public AudioClip bombBeepClip;
        public AudioClip bombExplodeClip;
        public AudioClip ultReadyClip;
        public AudioClip ultCastClip;
        public AudioClip specialSlamClip;
        /// <summary>风暴云落地雷声（P41，Inkwave storm_thunder 同构）。</summary>
        public AudioClip[] stormThunderClips;

        [Header("击杀与复活")]
        public AudioClip killClip;
        public AudioClip deathClip;
        public AudioClip respawnClip;

        [Header("比赛与UI音效")]
        public AudioClip countdownTickClip;
        public AudioClip matchStartClip;
        public AudioClip matchEndClip;
        public AudioClip resultWinClip;
        public AudioClip resultLoseClip;
        public AudioClip uiClickClip;
        public AudioClip uiHoverClip;
        public AudioClip uiConfirmClip;
        public AudioClip uiToggleClip;
        public AudioClip uiBackClip;
        public AudioClip uiSliderClip;
        public AudioClip uiErrorClip;

        [Header("HUD 低墨告警（P4-C）")]
        public AudioClip lowInkWarningClip;

        [Header("比赛流程提示音（P5-C 无语音版）")]
        public AudioClip goCueClip;
        public AudioClip oneMinuteCueClip;
        /// <summary>READY 建构音：进入 Ready 阶段（墨幕拉起前）播，和 Countdown 的蓄势音错开。</summary>
        public AudioClip readyClip;
        /// <summary>时间到：matchEndClip 缺失时的兜底终场提示。</summary>
        public AudioClip timesUpClip;

        [Header("双段动态 BGM（P5-C）")]
        public AudioClip bgmMainClip;
        public AudioClip bgmFinalClip;
        public AudioMixConfigSO mixConfig;

        [Header("循环音效（P1-Loop）")]
        public AudioClip loopRollerClip;
        public AudioClip loopSplatlingSpinClip;
        public AudioClip loopSwimClip;
        public AudioClip loopClimbClip;
        public AudioClip loopEnemyInkSizzleClip;
        public AudioClip loopStormRainClip;
        public AudioClip loopChargerChargeClip;

        [Header("涂地补充（P-Audio：墨花分档 / 打墙 / 泵）")]
        public AudioClip[] splatSmallClips;
        public AudioClip[] splatBigClips;
        public AudioClip[] inkHitWallClips;
        public AudioClip[] blasterPumpClips;
        public AudioClip splatlingReadyClip;
        public AudioClip stormWindClip;

        [Header("战斗状态提示（P-Audio）")]
        public AudioClip spawnProtectClip;
        public AudioClip lowHealthClip;
        public AudioClip respawnTickClip;

        [Header("战局提示（P-Audio）")]
        public AudioClip leadGainClip;
        public AudioClip leadLoseClip;
        public AudioClip final10sClip;
        public AudioClip enemySpecialWarnClip;

        [Header("操作反馈（P-Audio）")]
        public AudioClip weaponSwitchClip;
        public AudioClip chargeCancelClip;
        public AudioClip bombBounceClip;
        public AudioClip[] reticleLockClips;
        public AudioClip pauseOpenClip;
        public AudioClip pauseCloseClip;

        private AudioSource[] _pool;
        private int _poolCursor;
        private float _lastGroundHitTime;
        private float _lastEmptyClickTime;
        private float _lastRefillTickTime;
        private float _lastFootstepTime;
        private float _lastSwimSplashTime;
        private float _lastHurtTime;
        private float _lastHitMarkerTime;
        private float _lastUiSoundTime;
        private float _lastLowHealthTime;
        private float _lastReticleLockTime;
        private float _lastBombBounceTime;
        private float _lastInkHitWallTime;
        private float _lastSpawnProtectTime;
        private float _lastLeadChangeTime;

        private AudioSource _musicMainSource;
        private AudioSource _musicFinalSource;
        private AudioSource _matchCueSource;
        private Coroutine _musicRoutine;

        private float _musicMainGain;
        private float _musicFinalGain;
        private float _duckFactor = 1f;
        private float _duckTarget = 1f;
        private float _duckHoldUntil = -1f;

        private bool _matchMusicStarted;
        private bool _finalStage;
        private MatchManager _match;
        private AudioMixConfigSO _fallbackMix;
        private bool _warnedMissingMusic;

        private class LoopChannel
        {
            public LoopSfxKind Kind;
            public AudioSource Source;
            public Coroutine FadeRoutine;
            public bool IsFadingIn;
            public float TargetVolume;
            /// <summary>调用方附加音量乘法器（1 = 正常）。P41：风暴雨声随云层消散淡出。</summary>
            public float VolumeMultiplier = 1f;
        }

        private readonly Dictionary<LoopSfxKind, LoopChannel> _loopChannels = new Dictionary<LoopSfxKind, LoopChannel>();

        /// <summary>当前 BGM 相对目标音量的闪避系数（1 = 未闪避）。供调试/测试读取。</summary>
        public float CurrentDuckFactor => _duckFactor;

        /// <summary>是否已进入最后 1 分钟的决战段。</summary>
        public bool IsFinalStageMusic => _finalStage;

        private AudioMixConfigSO Mix
        {
            get
            {
                if (mixConfig != null)
                    return mixConfig;

                if (_fallbackMix == null)
                    _fallbackMix = ScriptableObject.CreateInstance<AudioMixConfigSO>();
                return _fallbackMix;
            }
        }

        private void Awake()
        {
            Instance = this;
            GameSettingsService.SfxVolumeChanged += OnSfxVolumeChanged;
            EnsureRuntimeClips();
#if UNITY_EDITOR
            EnsureEditorClips();
#endif
            // 16 条 voice：8 人混战时炸弹爆炸 + 大招 + 落墨 + 命中会同帧叠十几发，
            // 12 条会开始截断仍在发声的旧 clip（听感上是「音效被吃掉」）。
            BuildPool(16);
            BuildMusicSources();
            BuildLoopSources();
        }

        private void Start()
        {
            TryHookMatch();
        }

        private void OnDestroy()
        {
            GameSettingsService.SfxVolumeChanged -= OnSfxVolumeChanged;

            if (_match != null)
            {
                _match.PhaseChanged -= OnMatchPhaseChanged;
                _match.CountdownTicked -= OnCountdownTicked;
                _match = null;
            }

            StopMusicRoutine();
            HardStopAllLoops();

            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            if (_match == null)
                TryHookMatch();

            UpdateDuck(Time.unscaledDeltaTime);
            UpdateFinalStage();
            UpdateFinalTenSeconds();
            ApplyMusicVolume();
        }

        private void BuildPool(int count)
        {
            _pool = new AudioSource[count];
            for (int i = 0; i < count; i++)
            {
                var child = new GameObject($"SfxVoice_{i:00}");
                child.transform.SetParent(transform, false);
                var src = child.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                // P30 优先级阶梯：SFX(96) > 循环(128) > BGM(180)，流程提示 16。
                // 默认 128 会让 SFX 在发声数超限时不合理地先于 BGM 被抢占。
                src.priority = 96;
                _pool[i] = src;
            }
        }

        private void BuildMusicSources()
        {
            _musicMainSource = CreatePersistentSource("BGM_Main", true, 180);
            _musicFinalSource = CreatePersistentSource("BGM_Final", true, 180);
            // 比赛流程提示音独立通道：优先级高于战斗 SFX(96)，避免开赛/终场提示被枪声挤掉。
            _matchCueSource = CreatePersistentSource("MatchCue", false, 16);
        }

        private void BuildLoopSources()
        {
            var kinds = (LoopSfxKind[])System.Enum.GetValues(typeof(LoopSfxKind));
            for (int i = 0; i < kinds.Length; i++)
            {
                LoopSfxKind kind = kinds[i];
                var src = CreatePersistentSource($"LoopVoice_{kind}", true, 128);
                _loopChannels[kind] = new LoopChannel
                {
                    Kind = kind,
                    Source = src,
                    FadeRoutine = null,
                    IsFadingIn = false,
                    TargetVolume = 0f
                };
            }
        }

        /// <summary>
        /// EditMode 测试或手动 AddComponent 时不会触发 Awake，这里做惰性构建，
        /// 保证 GetLoopSource / PlayLoop 在任何调用路径下都有专属通道。
        /// </summary>
        private void EnsureLoopSources()
        {
            if (_loopChannels.Count == 0)
                BuildLoopSources();
        }

        private AudioSource CreatePersistentSource(string sourceName, bool loop, int priority)
        {
            var go = new GameObject(sourceName);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = loop;
            src.spatialBlend = 0f;
            src.volume = 0f;
            src.priority = priority;
            return src;
        }

        // ---------------------------------------------------------------- 比赛流程挂接

        private void TryHookMatch()
        {
            MatchManager match = MatchManager.Instance;
            if (match == null || match == _match)
                return;

            if (_match != null)
            {
                _match.PhaseChanged -= OnMatchPhaseChanged;
                _match.CountdownTicked -= OnCountdownTicked;
            }

            _match = match;
            _match.PhaseChanged += OnMatchPhaseChanged;
            _match.CountdownTicked += OnCountdownTicked;
            LogAudio("已挂接 MatchManager 比赛阶段");

            if (_match.CurrentPhase == MatchManager.Phase.Playing)
                StartMatchMusic();
        }

        private void OnMatchPhaseChanged(MatchManager.Phase phase)
        {
            switch (phase)
            {
                case MatchManager.Phase.Ready:
                    // 从对局/结算返回主菜单：停掉战斗 BGM 与循环音，保持开局时的安静状态
                    ResetMusicForNewMatch();
                    StopAllLoops();
                    // READY 建构音（旧版 ready_1 烘了却从未接线）：放在 Ready 而不是 Countdown，
                    // 免得和墨幕入场的 match_start 蓄势音叠在一起抢拍。
                    PlayMatchCue(readyClip, 0f);
                    break;

                case MatchManager.Phase.Countdown:
                    ResetMusicForNewMatch();
                    StopAllLoops();
                    // 墨幕入场期间只播「蓄势」音效，GO 号角留到 Playing 首帧，避免两段提示叠在一起
                    PlayMatchCue(matchStartClip);
                    break;

                case MatchManager.Phase.Playing:
                    StartMatchMusic();
                    PlayMatchCue(goCueClip);
                    break;

                case MatchManager.Phase.Finished:
                    // match_end 是既定的终场定格音（含裁判双音哨）；times_up 只作兜底，
                    // 避免两只哨音叠成 flam。
                    PlayMatchCue(matchEndClip != null ? matchEndClip : timesUpClip);
                    FadeOutMusic(Mix.bgmFadeOutSeconds);
                    StopAllLoops();
                    break;
            }
        }

        private void OnCountdownTicked(int shown)
        {
            // 旧 3-2-1 倒计时模式（introSeconds < 0）的逐秒提示：只留滴答音效，不再有人声。
            if (shown == 3)
                PlayMatchCue(countdownTickClip, 0f);
            else if (shown == 2)
                PlayMatchCue(countdownTickClip, 0f);
            else if (shown == 1)
                PlayMatchCue(countdownTickClip, 0f);
        }

        private void UpdateFinalStage()
        {
            if (_finalStage || !_matchMusicStarted || _match == null)
                return;

            if (_match.CurrentPhase != MatchManager.Phase.Playing)
                return;

            if (_match.RemainingSeconds > Mix.finalStageLeadSeconds)
                return;

            _finalStage = true;
            PlayMatchCue(oneMinuteCueClip);
            CrossfadeToFinalMusic();
        }

        private bool _final10sCuePlayed;

        /// <summary>最后 10 秒的紧迫提示（旧版只有 1 分钟那一档，末段没有递进）。</summary>
        private void UpdateFinalTenSeconds()
        {
            if (_final10sCuePlayed || !_matchMusicStarted || _match == null)
                return;

            if (_match.CurrentPhase != MatchManager.Phase.Playing)
                return;

            if (_match.RemainingSeconds > 10f)
                return;

            _final10sCuePlayed = true;
            PlayFinal10s();
            Duck(Mix.matchCueDuckAmount, 1.2f);
        }

        // ---------------------------------------------------------------- 动态 BGM

        private void ResetMusicForNewMatch()
        {
            StopMusicRoutine();
            StopSource(_musicMainSource);
            StopSource(_musicFinalSource);
            StopSource(_matchCueSource);
            _musicMainGain = 0f;
            _musicFinalGain = 0f;
            _duckFactor = 1f;
            _duckTarget = 1f;
            _duckHoldUntil = -1f;
            _matchMusicStarted = false;
            _finalStage = false;
            _final10sCuePlayed = false;
            ApplyMusicVolume();
        }

        private void StartMatchMusic()
        {
            if (_matchMusicStarted)
                return;

            _matchMusicStarted = true;
            _finalStage = false;
            StopMusicRoutine();
            StopSource(_musicMainSource);
            StopSource(_musicFinalSource);

            if (bgmMainClip != null && _musicMainSource != null)
            {
                _musicMainGain = 0f;
                _musicFinalGain = 0f;
                _musicMainSource.clip = bgmMainClip;
                _musicMainSource.time = 0f;
                _musicMainSource.Play();
                _musicRoutine = StartCoroutine(FadeRoutine(Mix.bgmCrossfadeSeconds, 0f, 1f, 0f, 0f, false));
                LogAudio($"主阶段 BGM 开始：{bgmMainClip.name} ({bgmMainClip.length:F1}s)");
            }
            else if (bgmFinalClip != null && _musicFinalSource != null)
            {
                _musicMainGain = 0f;
                _musicFinalGain = 0f;
                _musicFinalSource.clip = bgmFinalClip;
                _musicFinalSource.time = 0f;
                _musicFinalSource.Play();
                _musicRoutine = StartCoroutine(FadeRoutine(Mix.bgmCrossfadeSeconds, 0f, 0f, 0f, 1f, false));
                LogAudio($"BGM 仅有决战段，直接从决战段开始：{bgmFinalClip.name}");
            }
            else if (!_warnedMissingMusic)
            {
                _warnedMissingMusic = true;
                LogAudio("未绑定 BGM（bgmMainClip/bgmFinalClip 均为空），跳过对局音乐");
            }
        }

        private void CrossfadeToFinalMusic()
        {
            if (bgmFinalClip == null || _musicFinalSource == null)
            {
                LogAudio("决战段 BGM 未绑定，最后 1 分钟保持主阶段曲目");
                return;
            }

            StopMusicRoutine();
            _musicFinalSource.clip = bgmFinalClip;
            _musicFinalSource.time = 0f;
            _musicFinalSource.volume = 0f;
            _musicFinalSource.Play();
            _musicRoutine = StartCoroutine(FadeRoutine(
                Mix.bgmCrossfadeSeconds, _musicMainGain, 0f, 0f, 1f, false));
            LogAudio($"决战段 BGM 交叉淡化：{bgmFinalClip.name}，{Mix.bgmCrossfadeSeconds:F2}s");
        }

        private void FadeOutMusic(float duration)
        {
            StopMusicRoutine();
            _musicRoutine = StartCoroutine(FadeRoutine(
                duration, _musicMainGain, 0f, _musicFinalGain, 0f, true));
            LogAudio($"BGM 淡出 {duration:F2}s");
        }

        private IEnumerator FadeRoutine(float duration, float fromMain, float toMain,
            float fromFinal, float toFinal, bool stopSources)
        {
            duration = Mathf.Max(0.01f, duration);
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                k = k * k * (3f - 2f * k);
                _musicMainGain = Mathf.Lerp(fromMain, toMain, k);
                _musicFinalGain = Mathf.Lerp(fromFinal, toFinal, k);
                ApplyMusicVolume();
                yield return null;
            }

            _musicMainGain = toMain;
            _musicFinalGain = toFinal;
            ApplyMusicVolume();

            if (stopSources)
            {
                StopSource(_musicMainSource);
                StopSource(_musicFinalSource);
                _matchMusicStarted = false;
            }
            else
            {
                if (toMain <= 0.001f)
                    StopSource(_musicMainSource);
                if (toFinal <= 0.001f)
                    StopSource(_musicFinalSource);
            }

            _musicRoutine = null;
        }

        private void ApplyMusicVolume()
        {
            // 用户「音乐音量」分轨（主音量仍由 AudioListener.volume 总控）；
            // 本方法每帧由 Update() 调用，拖动滑条即时生效。
            float volume = Mix.bgmVolume * _duckFactor * GameSettingsService.MusicVolume;
            if (_musicMainSource != null)
                _musicMainSource.volume = volume * _musicMainGain;
            if (_musicFinalSource != null)
                _musicFinalSource.volume = volume * _musicFinalGain;
        }

        private void StopMusicRoutine()
        {
            if (_musicRoutine != null)
            {
                StopCoroutine(_musicRoutine);
                _musicRoutine = null;
            }
        }

        /// <summary>
        /// 「音效音量」变化时重算在播循环音的目标音量：循环音是长驻音源，只在 PlayLoop 时
        /// 定价的话拖滑条不会生效。有淡入淡出协程的通道取消协程直接跳变（循环音为氛围音，跳变不可闻）。
        /// </summary>
        private void OnSfxVolumeChanged(float volume)
        {
            foreach (var kvp in _loopChannels)
            {
                LoopChannel channel = kvp.Value;
                if (channel == null || channel.Source == null || !channel.Source.isPlaying)
                    continue;

                if (channel.FadeRoutine != null)
                {
                    StopCoroutine(channel.FadeRoutine);
                    channel.FadeRoutine = null;
                }

                channel.IsFadingIn = false;
                float newTarget = GetLoopBaseVolume(channel.Kind) * Mix.loopMasterVolume * volume * channel.VolumeMultiplier;
                channel.TargetVolume = newTarget;
                channel.Source.volume = newTarget;
            }
        }

        /// <summary>
        /// 设置循环音的附加音量乘法器（0~1，P41）：风暴雨声随云层消散淡出用。
        /// 立即跳变生效（与音效音量变化同一处理）；StopLoop 复位为 1。不改变 PlayLoop 的定价基准。
        /// </summary>
        public void SetLoopVolume(LoopSfxKind kind, float multiplier)
        {
            EnsureLoopSources();
            if (!_loopChannels.TryGetValue(kind, out LoopChannel channel) || channel.Source == null)
                return;

            channel.VolumeMultiplier = Mathf.Clamp01(multiplier);
            if (!channel.Source.isPlaying)
                return;

            if (channel.FadeRoutine != null)
            {
                StopCoroutine(channel.FadeRoutine);
                channel.FadeRoutine = null;
            }

            channel.IsFadingIn = false;
            float newTarget = GetLoopBaseVolume(channel.Kind) * Mix.loopMasterVolume * GameSettingsService.SfxVolume * channel.VolumeMultiplier;
            channel.TargetVolume = newTarget;
            channel.Source.volume = newTarget;
        }

        private static void StopSource(AudioSource src)
        {
            if (src == null)
                return;

            src.Stop();
            src.clip = null;
            src.volume = 0f;
        }

        // ---------------------------------------------------------------- Ducking

        public void Duck(float amount, float holdSeconds)
        {
            float target = 1f - Mathf.Clamp(amount, 0f, 0.95f);
            _duckTarget = Mathf.Min(_duckTarget, target);
            _duckHoldUntil = Mathf.Max(_duckHoldUntil,
                Time.unscaledTime + Mathf.Max(0f, holdSeconds) + Mix.duckTailSeconds);
            LogAudio($"BGM Duck → {target:P0}（保持 {holdSeconds:F2}s）");
        }

        private void UpdateDuck(float deltaTime)
        {
            if (Time.unscaledTime >= _duckHoldUntil)
                _duckTarget = 1f;

            float speed = _duckTarget < _duckFactor
                ? 1f / Mathf.Max(0.01f, Mix.duckAttackSeconds)
                : 1f / Mathf.Max(0.01f, Mix.duckReleaseSeconds);

            _duckFactor = Mathf.MoveTowards(_duckFactor, _duckTarget, speed * deltaTime);
        }

        /// <summary>
        /// 兼容旧签名：流程提示已全部改为音效，本方法转发到 <see cref="PlayMatchCue"/>。
        /// duckAmount 传负值表示使用配置里的默认闪避量。
        /// </summary>
        public void PlayAnnouncement(AudioClip clip, float duckAmount = -1f)
        {
            PlayMatchCue(clip, duckAmount);
        }

        /// <summary>
        /// 播放比赛流程提示音（开赛蓄势 / GO / 一分钟 / 终场）。
        /// 走独立通道，优先级高于战斗 SFX，不会被 8 人混战的枪声挤掉；
        /// 同时按流程提示音闪避量下压 BGM，给提示让出频段。
        /// </summary>
        public void PlayMatchCue(AudioClip clip, float duckAmount = -1f)
        {
            if (clip == null || _matchCueSource == null)
                return;

            _matchCueSource.volume = Mix.matchCueVolume * GameSettingsService.SfxVolume;
            _matchCueSource.clip = clip;
            _matchCueSource.Play();
            Duck(duckAmount < 0f ? Mix.matchCueDuckAmount : duckAmount, clip.length);
            LogAudio($"流程提示：{clip.name} ({clip.length:F2}s)");
        }

        // ---------------------------------------------------------------- 原有 SFX 接口（保持不变）

        public void PlayShot()
        {
            PlayRandom(shotClips, Mix.shotVolume, 0.94f, 1.08f);
        }

        public void PlayInkHitGround(float volumeScale = 1f)
        {
            if (Time.unscaledTime - _lastGroundHitTime < 0.045f)
                return;

            _lastGroundHitTime = Time.unscaledTime;
            PlayRandom(inkHitGroundClips, 0.36f * volumeScale, 0.90f, 1.12f);
        }

        /// <summary>墨点撞击音的最大可听距离（米）。8 人混战时远处 Bot 的落墨不该和脚下一样响。</summary>
        private const float InkHitMaxDistance = 26f;

        /// <summary>近处落墨的判定半径（米）：这个距离内不受全局 45ms 节流影响，保证自己的命中一定听得见。</summary>
        private const float InkHitNearDistance = 6f;

        /// <summary>
        /// 按世界位置播放落墨音（P16）：距本地玩家越远越轻、超出 26m 直接不播；
        /// 近处（≤6m）不受全局节流，避免自己的关键命中被远处 Bot 的落墨声挤掉。
        /// 墨斑越大音高越低，读得出「这一下更重」。
        /// </summary>
        public void PlayInkHitGround(Vector3 worldPos, float volumeScale, float impactRadius = 0f)
        {
            float attenuation = 1f;
            bool near = true;

            P1.Player.PlayerController player = P1.Player.PlayerController.LocalPlayer;
            if (player != null)
            {
                float distance = Vector3.Distance(worldPos, player.transform.position);
                if (distance > InkHitMaxDistance)
                    return;

                attenuation = 1f - distance / InkHitMaxDistance;
                attenuation *= attenuation;
                near = distance <= InkHitNearDistance;
            }

            if (!near && Time.unscaledTime - _lastGroundHitTime < 0.045f)
                return;

            _lastGroundHitTime = Time.unscaledTime;

            float size01 = Mathf.Clamp01((impactRadius - 0.3f) / 1.6f);
            float volume = 0.36f * Mathf.Clamp01(volumeScale) * (0.25f + 0.9f * attenuation);
            float pitchMin = Mathf.Lerp(1.10f, 0.90f, size01);
            PlayRandom(inkHitGroundClips, volume, pitchMin, pitchMin + 0.16f);
        }

        public void PlayHitEnemy()
        {
            PlayRandom(hitEnemyClips, 0.65f, 0.95f, 1.05f);
        }

        /// <summary>墨拍身体音的最大可听距离（米），与落墨音一致：远处 Bot 互殴不该和脚下一样响。</summary>
        private const float InkHitBodyMaxDistance = 26f;

        private float _lastBodyHitTime = -99f;

        /// <summary>
        /// 墨水拍打身体的音效（P6-B，对齐 Inkwave ink_hit_body）：按世界位置做距离衰减，
        /// 重击（≥60 伤害）音高下沉到 0.8 读作「打得很实」，轻击 1.05。
        /// UI 层的 hit_marker tick 由准星负责（PlayHitMarker），两层并行、各用各的 clip。
        /// </summary>
        public void PlayInkHitBody(Vector3 worldPos, float damage)
        {
            float attenuation = 1f;

            P1.Player.PlayerController player = P1.Player.PlayerController.LocalPlayer;
            if (player != null)
            {
                float distance = Vector3.Distance(worldPos, player.transform.position);
                if (distance > InkHitBodyMaxDistance)
                    return;

                attenuation = 1f - distance / InkHitBodyMaxDistance;
                attenuation *= attenuation;
            }

            if (Time.unscaledTime - _lastBodyHitTime < 0.03f)
                return;

            _lastBodyHitTime = Time.unscaledTime;

            float heavy01 = Mathf.Clamp01((damage - 30f) / 60f);
            float pitch = Mathf.Lerp(1.05f, 0.80f, heavy01);
            float volume = Mix.hitBodyVolume * (0.25f + 0.9f * attenuation);
            PlayRandom(hitEnemyClips, volume, pitch * 0.99f, pitch * 1.01f);
        }

        public void PlayInkEmpty()
        {
            if (Time.unscaledTime - _lastEmptyClickTime < 0.22f)
                return;

            _lastEmptyClickTime = Time.unscaledTime;
            PlayOne(inkEmptyClip, 0.55f, 0.98f, 1.04f);
        }

        public void PlaySwimRefillTick()
        {
            if (Time.unscaledTime - _lastRefillTickTime < 0.28f)
                return;

            _lastRefillTickTime = Time.unscaledTime;
            PlayOne(inkRefillClip, 0.30f, 0.95f, 1.10f);
        }

        public void PlayUltReady() => PlayOne(ultReadyClip, 0.75f, 1f, 1f);

        public void PlayUltCast() => PlayUltCast(true);

        public void PlayUltCast(bool duckMusic)
        {
            PlayOne(ultCastClip, 0.85f, 0.98f, 1.02f);
            if (duckMusic)
                Duck(Mix.ultDuckAmount, 0.55f);
        }

        public void PlayKill() => PlayOne(killClip, 0.80f, 0.98f, 1.04f);

        /// <summary>
        /// 击杀确认音（P6-B，对齐 Inkwave splat_enemy 的 sparkle chime 语义）：
        /// kill 主音之上叠一层 ui_confirm 双音琶音，连杀越高越亮（音量与音高逐层加码）。
        /// <paramref name="streak"/> 为连杀数（1 = 首杀）。
        /// </summary>
        public void PlayKill(int streak)
        {
            // 旧实现：uiConfirmClip 为空时把保底设成 killClip，同一帧把击杀音播两遍
            // （相位叠加成怪音）；现在统一走 PlayKillInternal，缺闪音就只播主音。
            PlayKillInternal(streak, 1f);
        }

        public void PlayDeath()
        {
            PlayOne(deathClip, Mix.deathVolume, 0.96f, 1.02f);

            var player = P1.Player.PlayerController.LocalPlayer;
            if (player != null && player.Health != null && !player.Health.IsAlive)
                Duck(Mix.deathDuckAmount, 0.9f);
        }

        /// <summary>
        /// 队友阵亡提示（P6-B，对齐 Inkwave ally_splatted 的语义）：
        /// 低音量 + 升调的死亡音，读作「远处队友倒下了」而不是自己阵亡。
        /// </summary>
        public void PlayAllyDeath()
        {
            PlayOne(deathClip, 0.28f, 1.12f, 1.16f);
        }

        public void PlayRespawn() => PlayOne(respawnClip, 0.60f, 0.98f, 1.04f);

        // ---------------------------------------------------------------- 新增 SFX 扩展接口

        /// <summary>按武器分类播放专属开火音，无专属音时优雅回落至 shotClips。</summary>
        public void PlayWeaponShot(WeaponKind kind)
        {
            PlayWeaponShot(kind, 1f);
        }

        /// <summary>
        /// P28：蓄力刻度参与开火音（蓄力狙满蓄更沉、更重；其余武器 strength=1 时与旧行为一致）。
        /// </summary>
        public void PlayWeaponShot(WeaponKind kind, float strength01)
        {
            float strength = Mathf.Clamp01(strength01);

            // P30 战斗 ducking：持续开火期间把 BGM 稳定压低一档（默认 -18%），停火后自动恢复。
            // 与流程提示/大招 duck 取 min 合并，不会互相抵消。
            Duck(Mix.combatDuckAmount, Mix.combatDuckHoldSeconds);

            switch (kind)
            {
                case WeaponKind.Roller:
                    if (rollerFlickClips != null && rollerFlickClips.Length > 0)
                        PlayRandom(rollerFlickClips, Mix.rollerFlickVolume, 0.94f, 1.06f);
                    else
                        PlayShot();
                    break;

                case WeaponKind.Charger:
                    if (chargerShotClip != null)
                    {
                        PlayOne(
                            chargerShotClip,
                            Mathf.Lerp(Mix.chargerShotVolumeMin, Mix.chargerShotVolumeMax, strength),
                            Mathf.Lerp(1.06f, 0.94f, strength),
                            Mathf.Lerp(1.08f, 0.96f, strength));
                    }
                    else
                        PlayShot();
                    break;

                case WeaponKind.Blaster:
                    if (blasterShotClips != null && blasterShotClips.Length > 0)
                        PlayRandom(blasterShotClips, Mix.blasterShotVolume, 0.94f, 1.06f);
                    else
                        PlayShot();
                    break;

                case WeaponKind.Slosher:
                    if (sloshThrowClip != null)
                        PlayOne(sloshThrowClip, Mix.sloshThrowVolume, 0.95f, 1.05f);
                    else
                        PlayShot();
                    break;

                case WeaponKind.Shooter:
                case WeaponKind.Dualies:
                case WeaponKind.Splatling:
                default:
                    PlayShot();
                    break;
            }
        }

        // ---------------------------------------------------------------- Bot 远场开火（P30）

        private float _lastBotShotTime = -99f;

        /// <summary>
        /// Bot/队友的远场开火音：按与本地玩家距离做平方衰减，超出 <see cref="AudioMixConfigSO.botShotMaxDistance"/> 不播；
        /// 全局节流（默认 50ms）保证 7 个 Bot 齐射也不会叠成音墙。
        /// 音量上限（默认 0.30）严格低于本地开火（0.58~0.72），只提供战场方位感与氛围。
        /// </summary>
        public void PlayWeaponShotAt(Vector3 worldPos, WeaponKind kind, float strength01 = 1f)
        {
            P1.Player.PlayerController player = P1.Player.PlayerController.LocalPlayer;
            if (player == null)
                return;

            float distance = Vector3.Distance(worldPos, player.transform.position);
            if (distance > Mix.botShotMaxDistance)
                return;

            if (Time.unscaledTime - _lastBotShotTime < Mix.botShotThrottleSeconds)
                return;

            _lastBotShotTime = Time.unscaledTime;

            float attenuation = 1f - distance / Mix.botShotMaxDistance;
            attenuation *= attenuation;
            float volume = Mix.botShotVolume * (0.20f + 0.80f * attenuation);

            AudioClip clip = PickShotClip(kind);
            if (clip == null)
                return;

            // 远场音高整体微降（距离感的低成本近似），强度越大越沉
            float pitch = Random.Range(0.94f, 1.02f) * Mathf.Lerp(1.02f, 0.96f, Mathf.Clamp01(strength01));
            PlayOne(clip, volume, pitch, pitch * 1.01f);
        }

        private AudioClip PickShotClip(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.Roller: return PickRandom(rollerFlickClips);
                case WeaponKind.Charger: return chargerShotClip != null ? chargerShotClip : PickRandom(shotClips);
                case WeaponKind.Blaster: return PickRandom(blasterShotClips);
                case WeaponKind.Slosher: return sloshThrowClip != null ? sloshThrowClip : PickRandom(shotClips);
                default: return PickRandom(shotClips);
            }
        }

        private static AudioClip PickRandom(AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0)
                return null;

            return clips[Random.Range(0, clips.Length)];
        }

        public void PlayBombThrow()
        {
            AudioClip clip = bombThrowClip != null ? bombThrowClip : (shotClips != null && shotClips.Length > 0 ? shotClips[0] : null);
            PlayOne(clip, 0.50f, 0.95f, 1.05f);
        }

        /// <summary>投掷炸弹（按世界位置衰减）：8 人同时扔雷时不再全场一个响度。</summary>
        public void PlayBombThrow(Vector3 worldPos)
        {
            float gain;
            if (!TryEventGain(worldPos, 24f, out gain))
                return;

            PlayOne(bombThrowClip, 0.50f * gain, 0.95f, 1.05f);
        }

        public void PlayBombBeep()
        {
            PlayOne(bombBeepClip, 0.40f, 1.0f, 1.0f);
        }

        public void PlayBombExplode()
        {
            PlayOne(bombExplodeClip, 0.85f, 0.96f, 1.04f);
        }

        public void PlaySpecialCast(SpecialKind kind) => PlaySpecialCast(kind, true);

        public void PlaySpecialCast(SpecialKind kind, bool duckMusic)
        {
            if (kind == SpecialKind.Slam)
            {
                AudioClip clip = specialSlamClip != null ? specialSlamClip : ultCastClip;
                PlayOne(clip, 0.90f, 0.98f, 1.02f);
                if (duckMusic)
                    Duck(Mix.ultDuckAmount, 0.70f);
            }
            else
            {
                AudioClip clip = ultCastClip;
                PlayOne(clip, 0.85f, 0.98f, 1.02f);
                if (duckMusic)
                    Duck(Mix.ultDuckAmount, 0.55f);
            }
        }

        /// <summary>风暴云落地雷声（P41）：随机变体，音量对齐 Inkwave storm_thunder 的 0.6。</summary>
        public void PlayStormThunder()
        {
            PlayOne(PickRandom(stormThunderClips), 0.60f, 0.94f, 1.06f);
        }

        public void PlayHurt()
        {
            if (Time.unscaledTime - _lastHurtTime < Mix.hurtThrottleSeconds)
                return;

            _lastHurtTime = Time.unscaledTime;
            PlayOne(hurtClip, 0.70f, 0.95f, 1.05f);
        }

        public void PlayJump()
        {
            PlayOne(jumpClip, Mix.jumpVolume, 0.97f, 1.03f);
        }

        public void PlayLand()
        {
            PlayOne(landClip, Mix.landVolume, 0.96f, 1.04f);
        }

        /// <summary>双枪战术翻滚：下扫「唰」+ 低频体感（P40），双变体随机音高。</summary>
        public void PlayDodgeRoll()
        {
            PlayOne(dodgeRollClip, Mix.dodgeRollVolume, 0.92f, 1.08f);
        }

        public void PlaySquidIn()
        {
            PlayOne(squidInClip, Mix.squidTransformVolume, 0.98f, 1.02f);
        }

        public void PlaySquidOut()
        {
            PlayOne(squidOutClip, Mix.squidTransformVolume, 0.98f, 1.02f);
        }

        public void PlayFootstep(bool onInk, bool onEnemyInk)
        {
            if (Time.unscaledTime - _lastFootstepTime < Mix.footstepThrottleSeconds)
                return;

            _lastFootstepTime = Time.unscaledTime;

            if (onEnemyInk)
                PlayRandom(stepEnemyClips, Mix.stepEnemyInkVolume, 0.95f, 1.05f);
            else if (onInk)
                PlayRandom(stepInkBubbleClips, Mix.stepInkVolume, 0.95f, 1.05f);
            else
                PlayRandom(stepDryClips, Mix.stepDryVolume, 0.92f, 1.08f);
        }

        public void PlaySwimSplash()
        {
            if (Time.unscaledTime - _lastSwimSplashTime < Mix.swimSplashThrottleSeconds)
                return;

            _lastSwimSplashTime = Time.unscaledTime;
            PlayRandom(swimSplashClips, Mix.swimSplashVolume, 0.95f, 1.05f);
        }

        public void PlayHitMarker()
        {
            if (Time.unscaledTime - _lastHitMarkerTime < Mix.hitMarkerThrottleSeconds)
                return;

            _lastHitMarkerTime = Time.unscaledTime;
            PlayRandom(hitMarkerClips, Mix.hitMarkerVolume, 0.98f, 1.02f);
        }

        /// <summary>
        /// 带音高/音量的 hit_marker UI tick（P6-B）：命中确认的「哒」声由准星在 UI 层播放，
        /// 音高按连击半音曲线（HitFeedbackMath.ComboPitch）抬升；与 hitEnemyClips 的身体音分层。
        /// </summary>
        public void PlayHitMarker(float pitch, float volume)
        {
            if (Time.unscaledTime - _lastHitMarkerTime < Mix.hitMarkerThrottleSeconds)
                return;

            _lastHitMarkerTime = Time.unscaledTime;
            PlayRandom(hitMarkerClips, Mathf.Clamp(volume, 0f, 1f), pitch * 0.99f, pitch * 1.01f);
        }

        public void PlayUISound(UiSoundKind kind)
        {
            if (Time.unscaledTime - _lastUiSoundTime < Mix.uiThrottleSeconds)
                return;

            _lastUiSoundTime = Time.unscaledTime;

            switch (kind)
            {
                case UiSoundKind.Hover:
                    PlayOne(uiHoverClip, 0.35f, 0.98f, 1.02f);
                    break;
                case UiSoundKind.Confirm:
                    PlayOne(uiConfirmClip != null ? uiConfirmClip : uiClickClip, 0.55f, 1f, 1f);
                    break;
                case UiSoundKind.Toggle:
                    PlayOne(uiToggleClip != null ? uiToggleClip : uiClickClip, 0.50f, 0.98f, 1.02f);
                    break;
                case UiSoundKind.Back:
                    PlayOne(uiBackClip, 0.50f, 0.98f, 1.02f);
                    break;
                case UiSoundKind.Slider:
                    PlayOne(uiSliderClip, 0.40f, 0.98f, 1.02f);
                    break;
                case UiSoundKind.Error:
                    PlayOne(uiErrorClip, 0.60f, 1f, 1f);
                    break;
                case UiSoundKind.Click:
                default:
                    PlayOne(uiClickClip, 0.50f, 0.98f, 1.02f);
                    break;
            }
        }

        // ---------------------------------------------------------------- 远场衰减与新增事件音（P-Audio）

        /// <summary>一次性事件音的最大可听距离（米）。</summary>
        private const float EventMaxDistance = 30f;

        private float _lastRefillFullTime;

        /// <summary>
        /// 世界坐标事件音的衰减：超出 maxDistance 返回 false（不播），否则给 0.22~1 的增益。
        /// 与 <see cref="PlayInkHitGround(Vector3,float,float)"/> 的平方衰减同族，
        /// 但保留 0.22 的远处保底——敌人在地图对角放大招仍要听得见方位，只是不该和贴脸一样响。
        /// </summary>
        private bool TryEventGain(Vector3 worldPos, float maxDistance, out float gain)
        {
            gain = 1f;
            P1.Player.PlayerController player = P1.Player.PlayerController.LocalPlayer;
            if (player == null)
                return true;

            float distance = Vector3.Distance(worldPos, player.transform.position);
            if (distance > maxDistance)
                return false;

            float a = 1f - distance / maxDistance;
            gain = 0.22f + 0.78f * a * a;
            return true;
        }

        // ---- 炸弹 ----

        /// <summary>炸弹爆炸（按世界位置衰减；无参重载保持「本地满音量」旧语义）。</summary>
        public void PlayBombExplode(Vector3 worldPos)
        {
            float gain;
            if (!TryEventGain(worldPos, EventMaxDistance, out gain))
                return;

            PlayOne(bombExplodeClip, 0.85f * gain, 0.96f, 1.04f);
            if (gain > 0.9f) // 只有贴脸爆炸才压 BGM，远处的别抢我方音乐
                Duck(Mix.ultDuckAmount * 0.5f, 0.45f);
        }

        public void PlayBombBeep(Vector3 worldPos)
        {
            float gain;
            if (!TryEventGain(worldPos, EventMaxDistance, out gain))
                return;

            PlayOne(bombBeepClip, 0.40f * gain, 1f, 1f);
        }

        /// <summary>炸弹撞墙反弹（旧版完全静音）：短促干硬「嗒」，提示「炸弹还在弹」。</summary>
        public void PlayBombBounce(Vector3 worldPos)
        {
            float gain;
            if (!TryEventGain(worldPos, 18f, out gain))
                return;

            if (Time.unscaledTime - _lastBombBounceTime < Mix.bombBounceThrottleSeconds)
                return;

            _lastBombBounceTime = Time.unscaledTime;
            PlayOne(bombBounceClip, Mix.bombBounceVolume * gain, 0.94f, 1.06f);
        }

        // ---- 大招与击杀 ----

        /// <summary>按世界位置播放施放大招音：远处敌人大招只留方位感，不闪避我方 BGM。</summary>
        public void PlaySpecialCast(SpecialKind kind, Vector3 worldPos, bool duckMusic = true)
        {
            float gain;
            if (!TryEventGain(worldPos, EventMaxDistance + 6f, out gain))
                return;

            AudioClip clip = kind == SpecialKind.Slam
                ? (specialSlamClip != null ? specialSlamClip : ultCastClip)
                : ultCastClip;

            PlayOne(clip, 0.90f * gain, 0.98f, 1.02f);

            if (duckMusic && gain > 0.9f)
                Duck(Mix.ultDuckAmount, kind == SpecialKind.Slam ? 0.70f : 0.55f);
        }

        private void PlayKillInternal(int streak, float gain)
        {
            if (killClip != null)
                PlayOne(killClip, 0.80f * gain, 0.98f, 1.04f);

            // 连杀闪音只在有存在感且配了 ui_confirm 时叠。
            // 旧版在 uiConfirmClip 为空时回退成 killClip，同一帧把击杀音播两遍（相位叠加成怪音）。
            if (gain < 0.5f || uiConfirmClip == null)
                return;

            int bonus = Mathf.Clamp(Mathf.Max(0, streak) - 1, 0, 3);
            float chimePitch = 1.02f + 0.05f * bonus;
            PlayOne(uiConfirmClip, (0.24f + 0.05f * bonus) * gain, chimePitch, chimePitch * 1.01f);
        }

        /// <summary>按世界位置播放击杀音（远处队友击杀不该和自己的击杀一样响）。</summary>
        public void PlayKill(int streak, Vector3 worldPos)
        {
            float gain;
            if (!TryEventGain(worldPos, EventMaxDistance, out gain))
                return;

            PlayKillInternal(streak, gain);
        }

        public void PlayDeath(Vector3 worldPos)
        {
            float gain;
            if (!TryEventGain(worldPos, EventMaxDistance, out gain))
                return;

            PlayOne(deathClip, Mix.deathVolume * gain, 0.96f, 1.02f);
            if (gain > 0.9f)
                Duck(Mix.deathDuckAmount, 0.9f);
        }

        public void PlayStormThunder(Vector3 worldPos)
        {
            float gain;
            if (!TryEventGain(worldPos, EventMaxDistance + 10f, out gain))
                return;

            PlayOne(PickRandom(stormThunderClips), 0.60f * gain, 0.94f, 1.06f);
        }

        // ---- 移动 ----

        /// <summary>
        /// 起跳音（按世界位置）：旧版对所有角色都不加判定、按全局 2D 满音量播，
        /// 8 人同时跳跃时自己的起跳会被别人的盖住。Bot 的跳跃现在按距离衰减。
        /// </summary>
        public void PlayJump(Vector3 worldPos)
        {
            float gain;
            if (!TryEventGain(worldPos, 22f, out gain))
                return;

            PlayOne(jumpClip, Mix.jumpVolume * gain, 0.97f, 1.03f);
        }

        public void PlayDodgeRoll(Vector3 worldPos)
        {
            float gain;
            if (!TryEventGain(worldPos, 22f, out gain))
                return;

            PlayOne(dodgeRollClip, Mix.dodgeRollVolume * gain, 0.92f, 1.08f);
        }

        // ---- 涂地补充 ----

        /// <summary>
        /// 墨花分档：impactRadius 越大越重——≥1.8m 走 slosh_land（整桶倾倒的湿重），
        /// ≥1.2m 走 splat_big，否则 splat_small。
        /// slosh_land 旧版烘了却从没播；墨斑尺寸是它最自然的落点。
        /// </summary>
        public void PlaySplat(Vector3 worldPos, float impactRadius, float volumeScale = 1f)
        {
            float gain;
            if (!TryEventGain(worldPos, InkHitMaxDistance, out gain))
                return;

            AudioClip[] clips = impactRadius >= 1.8f
                ? sloshLandClips
                : (impactRadius >= 1.2f ? splatBigClips : splatSmallClips);

            PlayRandom(clips, Mix.splatVolume * Mathf.Clamp01(volumeScale) * gain, 0.92f, 1.08f);
        }

        /// <summary>墨水打墙（旧版只有落地一种墨音，打墙听起来像打地）。</summary>
        public void PlayInkHitWall(Vector3 worldPos, float volumeScale = 1f)
        {
            float gain;
            if (!TryEventGain(worldPos, InkHitMaxDistance, out gain))
                return;

            if (Time.unscaledTime - _lastInkHitWallTime < 0.04f)
                return;

            _lastInkHitWallTime = Time.unscaledTime;
            PlayRandom(inkHitWallClips,
                Mix.inkHitWallVolume * Mathf.Clamp01(volumeScale) * gain, 0.94f, 1.06f);
        }

        /// <summary>泼桶落墨重音（旧版烘了 slosh_land 却从没播）。</summary>
        public void PlaySloshLand(Vector3 worldPos, float volumeScale = 1f)
        {
            float gain;
            if (!TryEventGain(worldPos, EventMaxDistance, out gain))
                return;

            PlayRandom(sloshLandClips,
                Mix.sloshLandVolume * Mathf.Clamp01(volumeScale) * gain, 0.94f, 1.06f);
        }

        /// <summary>爆破枪空爆（旧版烘了 blaster_boom 却从没播）。</summary>
        public void PlayBlasterBoom(Vector3 worldPos)
        {
            float gain;
            if (!TryEventGain(worldPos, EventMaxDistance, out gain))
                return;

            PlayRandom(blasterBoomClips, Mix.blasterBoomVolume * gain, 0.94f, 1.06f);
        }

        /// <summary>爆破枪换气泵（跟随射速的机械音，只有本地玩家播）。</summary>
        public void PlayBlasterPump()
        {
            PlayRandom(blasterPumpClips, Mix.blasterPumpVolume, 0.96f, 1.04f);
        }

        public void PlaySplatlingReady()
        {
            PlayOne(splatlingReadyClip, Mix.splatlingReadyVolume, 0.98f, 1.02f);
        }

        public void PlayStormWind()
        {
            PlayOne(stormWindClip, Mix.stormWindVolume, 0.96f, 1.04f);
        }

        // ---- 状态与战局提示 ----

        /// <summary>出生保护生效：只播给本地玩家（别人的保护音会让人误判自己有无敌）。</summary>
        public void PlaySpawnProtect()
        {
            if (Time.unscaledTime - _lastSpawnProtectTime < 0.5f)
                return;

            _lastSpawnProtectTime = Time.unscaledTime;
            PlayOne(spawnProtectClip, Mix.spawnProtectVolume, 0.99f, 1.01f);
        }

        /// <summary>低血心跳：必须每次音高一致才读得成「危险」，所以不做随机音高。</summary>
        public void PlayLowHealth()
        {
            if (Time.unscaledTime - _lastLowHealthTime < Mix.lowHealthThrottleSeconds)
                return;

            _lastLowHealthTime = Time.unscaledTime;
            PlayOne(lowHealthClip, Mix.lowHealthVolume, 1f, 1f);
        }

        public void PlayRespawnTick()
        {
            PlayOne(respawnTickClip, Mix.respawnTickVolume, 0.99f, 1.01f);
        }

        /// <summary>比分反超提示：leading=true 我方领先（上行），false 被反超（下行）。</summary>
        public void PlayLeadChange(bool leading)
        {
            if (Time.unscaledTime - _lastLeadChangeTime < Mix.leadChangeThrottleSeconds)
                return;

            _lastLeadChangeTime = Time.unscaledTime;
            PlayOne(leading ? leadGainClip : leadLoseClip, Mix.leadChangeVolume, 1f, 1f);
        }

        /// <summary>终局最后 10 秒的紧迫提示（每分钟提示之外的第二档）。</summary>
        public void PlayFinal10s()
        {
            PlayOne(final10sClip, Mix.final10sVolume, 1f, 1f);
        }

        /// <summary>敌方大招来袭：远处报警，不闪避 BGM、不盖住自己的战斗音。</summary>
        public void PlayEnemySpecialWarn(Vector3 worldPos)
        {
            float gain;
            if (!TryEventGain(worldPos, EventMaxDistance + 10f, out gain))
                return;

            PlayOne(enemySpecialWarnClip, Mix.enemySpecialWarnVolume * gain, 1f, 1f);
        }

        // ---- 操作反馈 ----

        public void PlayWeaponSwitch()
        {
            PlayOne(weaponSwitchClip, Mix.weaponSwitchVolume, 0.97f, 1.03f);
        }

        /// <summary>
        /// 武器卡片 / 整备换枪的选择音：比普通 UI 点击更响，但**必须走同一节流**。
        /// 旧实现的两个界面各自直接调 PlayOne(uiClickClip, 0.75f)，绕过了 30ms 节流，
        /// 连点时会把点击音叠成爆音。
        /// </summary>
        public void PlayWeaponCardSelect()
        {
            if (Time.unscaledTime - _lastUiSoundTime < Mix.uiThrottleSeconds)
                return;

            _lastUiSoundTime = Time.unscaledTime;
            PlayOne(uiClickClip, 0.75f, 0.96f, 1.05f);
        }

        public void PlayChargeCancel()
        {
            PlayOne(chargeCancelClip, Mix.chargeCancelVolume, 0.98f, 1.02f);
        }

        public void PlayReticleLock()
        {
            if (Time.unscaledTime - _lastReticleLockTime < Mix.reticleLockThrottleSeconds)
                return;

            _lastReticleLockTime = Time.unscaledTime;
            PlayRandom(reticleLockClips, Mix.reticleLockVolume, 0.99f, 1.01f);
        }

        public void PlayPause(bool opening)
        {
            PlayOne(opening ? pauseOpenClip : pauseCloseClip, Mix.pauseVolume, 1f, 1f);
        }

        /// <summary>墨罐回满：由 InkCharacterMotor 在「未满 → 满」边沿触发，别每帧刷。</summary>
        public void PlayRefillFull()
        {
            if (Time.unscaledTime - _lastRefillFullTime < 0.8f)
                return;

            _lastRefillFullTime = Time.unscaledTime;
            PlayOne(refillFullClip, Mix.refillFullVolume, 0.99f, 1.01f);
        }

        // ---------------------------------------------------------------- 循环音效通道管线

        /// <summary>
        /// 蓄力狙循环音随蓄力进度的最低音量乘法器（WeaponAudioDriver 按 Charge01 插值用）。
        /// </summary>
        public float ChargerChargeRampMinVolume => Mix.chargerChargeRampMinVolume;

        /// <summary>
        /// 启动指定类型的循环音效：带淡入包络，同一循环重复 Play 不会叠音或重置播放位置。
        /// </summary>
        public void PlayLoop(LoopSfxKind kind)
        {
            PlayLoop(kind, 1f);
        }

        /// <summary>
        /// 启动指定类型的循环音效并附加音量乘法器（0~1，2026-11-08）：蓄力狙循环音随蓄力进度渐强用。
        /// 旧签名保留（等价于 multiplier=1）；StopLoop 仍会复位乘法器；淡入期间重复调用以更新后的乘法器续接。
        /// </summary>
        public void PlayLoop(LoopSfxKind kind, float volumeMultiplier)
        {
            if (!isActiveAndEnabled)
                return;

            EnsureLoopSources();
            if (!_loopChannels.TryGetValue(kind, out LoopChannel channel) || channel.Source == null)
                return;

            AudioClip clip = GetLoopClip(kind);
            if (clip == null)
                return;

            channel.VolumeMultiplier = Mathf.Clamp01(volumeMultiplier);

            if (channel.Source.isPlaying && channel.IsFadingIn)
                return;

            float baseVol = GetLoopBaseVolume(kind);
            float targetVol = baseVol * Mix.loopMasterVolume * GameSettingsService.SfxVolume * channel.VolumeMultiplier;

            if (channel.FadeRoutine != null)
            {
                StopCoroutine(channel.FadeRoutine);
                channel.FadeRoutine = null;
            }

            channel.IsFadingIn = true;
            channel.TargetVolume = targetVol;
            channel.Source.clip = clip;
            if (!channel.Source.isPlaying)
            {
                channel.Source.volume = 0f;
                channel.Source.Play();
            }

            if (Application.isPlaying && isActiveAndEnabled)
                channel.FadeRoutine = StartCoroutine(LoopFadeRoutine(channel, targetVol, Mix.loopFadeSeconds, false));
            else
                channel.Source.volume = targetVol;
        }

        /// <summary>
        /// 停止指定类型的循环音效：带平滑淡出，音量归零后自动 Stop 释放硬件资源；immediate 为 true 时立即硬停。
        /// </summary>
        public void StopLoop(LoopSfxKind kind, bool immediate = false)
        {
            EnsureLoopSources();
            if (!_loopChannels.TryGetValue(kind, out LoopChannel channel) || channel.Source == null)
                return;

            if (!channel.Source.isPlaying && channel.Source.volume <= 0.001f
                && !channel.IsFadingIn && channel.FadeRoutine == null)
                return;

            // 已在淡出：WeaponAudioDriver 每帧都会对未激活的循环音调 StopLoop，
            // 旧版每帧 Stop+Start 一次淡出协程（每帧一个迭代器分配），且淡出被无限重置。
            if (!immediate && !channel.IsFadingIn && channel.TargetVolume <= 0.0001f
                && channel.FadeRoutine != null)
                return;

            if (channel.FadeRoutine != null)
            {
                StopCoroutine(channel.FadeRoutine);
                channel.FadeRoutine = null;
            }

            channel.IsFadingIn = false;
            channel.TargetVolume = 0f;
            channel.VolumeMultiplier = 1f;
            if (!immediate && Application.isPlaying && isActiveAndEnabled)
            {
                channel.FadeRoutine = StartCoroutine(LoopFadeRoutine(channel, 0f, Mix.loopFadeSeconds, true));
            }
            else
            {
                channel.Source.Stop();
                channel.Source.clip = null;
                channel.Source.volume = 0f;
            }
        }

        public void StopAllLoops(bool immediate = false)
        {
            EnsureLoopSources();
            foreach (var kvp in _loopChannels)
            {
                StopLoop(kvp.Key, immediate);
            }
        }

        private void HardStopAllLoops()
        {
            foreach (var kvp in _loopChannels)
            {
                LoopChannel channel = kvp.Value;
                if (channel == null)
                    continue;

                if (channel.FadeRoutine != null)
                {
                    StopCoroutine(channel.FadeRoutine);
                    channel.FadeRoutine = null;
                }

                channel.IsFadingIn = false;
                channel.TargetVolume = 0f;

                if (channel.Source != null)
                {
                    channel.Source.Stop();
                    channel.Source.clip = null;
                    channel.Source.volume = 0f;
                }
            }
            _loopChannels.Clear();
        }

        public bool IsLoopPlaying(LoopSfxKind kind)
        {
            EnsureLoopSources();
            if (_loopChannels.TryGetValue(kind, out LoopChannel channel) && channel.Source != null)
            {
                return channel.IsFadingIn || channel.Source.isPlaying;
            }
            return false;
        }

        public AudioSource GetLoopSource(LoopSfxKind kind)
        {
            EnsureLoopSources();
            return _loopChannels.TryGetValue(kind, out LoopChannel channel) ? channel.Source : null;
        }

        private IEnumerator LoopFadeRoutine(LoopChannel channel, float targetVol, float duration, bool stopOnZero)
        {
            duration = Mathf.Max(0.01f, duration);
            float startVol = channel.Source.volume;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                channel.Source.volume = Mathf.Lerp(startVol, targetVol, k);
                yield return null;
            }

            channel.Source.volume = targetVol;
            if (stopOnZero && targetVol <= 0.001f)
            {
                channel.Source.Stop();
                channel.Source.clip = null;
            }
            channel.FadeRoutine = null;
        }

        private float GetLoopBaseVolume(LoopSfxKind kind)
        {
            switch (kind)
            {
                case LoopSfxKind.Roller: return Mix.rollerLoopVolume;
                case LoopSfxKind.SplatlingSpin: return Mix.splatlingSpinLoopVolume;
                case LoopSfxKind.Swim: return Mix.swimLoopVolume;
                case LoopSfxKind.Climb: return Mix.climbLoopVolume;
                case LoopSfxKind.EnemyInkSizzle: return Mix.enemyInkSizzleLoopVolume;
                case LoopSfxKind.StormRain: return Mix.stormRainLoopVolume;
                case LoopSfxKind.ChargerCharge: return Mix.chargerChargeLoopVolume;
                default: return 0.5f;
            }
        }

        private AudioClip GetLoopClip(LoopSfxKind kind)
        {
            switch (kind)
            {
                case LoopSfxKind.Roller: return loopRollerClip;
                case LoopSfxKind.SplatlingSpin: return loopSplatlingSpinClip;
                case LoopSfxKind.Swim: return loopSwimClip;
                case LoopSfxKind.Climb: return loopClimbClip;
                case LoopSfxKind.EnemyInkSizzle: return loopEnemyInkSizzleClip;
                case LoopSfxKind.StormRain: return loopStormRainClip;
                case LoopSfxKind.ChargerCharge: return loopChargerChargeClip;
                default: return null;
            }
        }

        // ---------------------------------------------------------------- 底层播放与辅助

        private void PlayRandom(AudioClip[] clips, float volume, float minPitch, float maxPitch)
        {
            if (clips == null || clips.Length == 0)
                return;

            AudioClip clip = clips[Random.Range(0, clips.Length)];
            PlayOne(clip, volume, minPitch, maxPitch);
        }

        public void PlayOne(AudioClip clip, float volume = 0.7f, float minPitch = 1f, float maxPitch = 1f)
        {
            if (clip == null || _pool == null || _pool.Length == 0)
                return;

            AudioSource src = _pool[_poolCursor];
            _poolCursor = (_poolCursor + 1) % _pool.Length;

            src.pitch = Random.Range(minPitch, maxPitch);
            // 全项目一次性音效（含 UI 音）的唯一收口：在此乘「音效音量」分轨。
            src.volume = volume * GameSettingsService.SfxVolume;
            src.clip = clip;
            src.Play();
        }

        private static void LogAudio(string message)
        {
#if UNITY_EDITOR || DEBUG
            Debug.Log("[P5-C][Audio] " + message);
#endif
        }

        /// <summary>
        /// 从 Resources/P1RuntimeAssets 回填未在场景中序列化的音效。
        /// 这是独立包（Player Build）里新音效可见的唯一入口；编辑器内随后还会走
        /// <see cref="EnsureEditorClips"/> 做路径兜底。
        /// </summary>
private void EnsureRuntimeClips()
        {
            RuntimeAssetLibrary library = RuntimeAssetLibrary.Load();
            if (library == null)
                return;

            // 变体数组一律按「族名」自动取全（变体数由 tools/bake_sfx.py 决定）；
            // 单发音效仍按具体文件名取。
            EnsureVariantSet(ref shotClips, library, "shot");
            EnsureVariantSet(ref inkHitGroundClips, library, "ink_hit_ground");
            EnsureVariantSet(ref splatSmallClips, library, "splat_small");
            EnsureVariantSet(ref splatBigClips, library, "splat_big");
            EnsureVariantSet(ref inkHitWallClips, library, "ink_hit_wall");
            EnsureVariantSet(ref hitEnemyClips, library, "hit_enemy");
            EnsureClip(ref inkEmptyClip, library, "ink_empty_1");
            EnsureClip(ref inkRefillClip, library, "ink_refill_1");

            EnsureVariantSet(ref rollerFlickClips, library, "roller_flick");
            EnsureClip(ref chargerChargeClip, library, "charger_charge_1");
            EnsureClip(ref chargerFullClip, library, "charger_full_1");
            EnsureClip(ref chargerShotClip, library, "charger_shot_1");
            EnsureVariantSet(ref blasterShotClips, library, "blaster_shot");
            EnsureVariantSet(ref blasterPumpClips, library, "blaster_pump");
            EnsureVariantSet(ref blasterBoomClips, library, "blaster_boom");
            EnsureClip(ref sloshThrowClip, library, "slosh_throw_1");
            EnsureVariantSet(ref sloshLandClips, library, "slosh_land");
            EnsureClip(ref splatlingReadyClip, library, "splatling_ready_1");

            EnsureClip(ref squidInClip, library, "squid_in_1");
            EnsureClip(ref squidOutClip, library, "squid_out_1");
            EnsureVariantSet(ref swimSplashClips, library, "swim_splash");
            EnsureVariantSet(ref stepDryClips, library, "step_dry");
            EnsureVariantSet(ref stepInkBubbleClips, library, "step_ink_bubble");
            EnsureVariantSet(ref stepEnemyClips, library, "step_enemy");
            EnsureClip(ref jumpClip, library, "jump_1");
            EnsureClip(ref landClip, library, "land_1");
            EnsureClip(ref dodgeRollClip, library, "dodge_roll_1");

            EnsureClip(ref hurtClip, library, "hurt_1");
            EnsureVariantSet(ref hitMarkerClips, library, "hit_marker");
            EnsureClip(ref refillFullClip, library, "refill_full_1");
            EnsureClip(ref spawnProtectClip, library, "spawn_protect_1");
            EnsureClip(ref lowHealthClip, library, "low_health_1");
            EnsureClip(ref respawnTickClip, library, "respawn_tick_1");

            EnsureClip(ref bombThrowClip, library, "bomb_throw_1");
            EnsureClip(ref bombBeepClip, library, "bomb_beep_1");
            EnsureClip(ref bombExplodeClip, library, "bomb_explode_1");
            EnsureClip(ref bombBounceClip, library, "bomb_bounce_1");
            EnsureClip(ref ultReadyClip, library, "ult_ready_1");
            EnsureClip(ref ultCastClip, library, "ult_cast_1");
            EnsureClip(ref specialSlamClip, library, "special_slam_1");
            EnsureVariantSet(ref stormThunderClips, library, "storm_thunder");
            EnsureClip(ref stormWindClip, library, "storm_wind_1");

            EnsureClip(ref killClip, library, "kill_1");
            EnsureClip(ref deathClip, library, "death_1");
            EnsureClip(ref respawnClip, library, "respawn_1");

            EnsureClip(ref countdownTickClip, library, "countdown_tick_1");
            EnsureClip(ref matchStartClip, library, "match_start_1");
            EnsureClip(ref goCueClip, library, "go_horn_1");
            EnsureClip(ref oneMinuteCueClip, library, "one_minute_1");
            EnsureClip(ref readyClip, library, "ready_1");
            EnsureClip(ref matchEndClip, library, "match_end_1");
            EnsureClip(ref timesUpClip, library, "times_up_1");
            EnsureClip(ref resultWinClip, library, "result_win_1");
            EnsureClip(ref resultLoseClip, library, "result_lose_1");
            EnsureClip(ref final10sClip, library, "final_10s_1");
            EnsureClip(ref leadGainClip, library, "lead_gain_1");
            EnsureClip(ref leadLoseClip, library, "lead_lose_1");
            EnsureClip(ref enemySpecialWarnClip, library, "enemy_special_warn_1");

            EnsureClip(ref uiClickClip, library, "ui_click_1");
            EnsureClip(ref uiHoverClip, library, "ui_hover_1");
            EnsureClip(ref uiConfirmClip, library, "ui_confirm_1");
            EnsureClip(ref uiToggleClip, library, "ui_toggle_1");
            EnsureClip(ref uiBackClip, library, "ui_back_1");
            EnsureClip(ref uiSliderClip, library, "ui_slider_1");
            EnsureClip(ref uiErrorClip, library, "ui_error_1");
            EnsureClip(ref lowInkWarningClip, library, "low_ink_warning_1");
            EnsureClip(ref pauseOpenClip, library, "pause_open_1");
            EnsureClip(ref pauseCloseClip, library, "pause_close_1");
            EnsureClip(ref weaponSwitchClip, library, "weapon_switch_1");
            EnsureClip(ref chargeCancelClip, library, "charge_cancel_1");
            EnsureVariantSet(ref reticleLockClips, library, "reticle_lock");

            EnsureClip(ref loopRollerClip, library, "roll_loop_1");
            EnsureClip(ref loopSplatlingSpinClip, library, "splatling_spin_loop_1");
            EnsureClip(ref loopSwimClip, library, "swim_loop_1");
            EnsureClip(ref loopClimbClip, library, "climb_loop_1");
            EnsureClip(ref loopEnemyInkSizzleClip, library, "enemy_ink_sizzle_loop_1");
            EnsureClip(ref loopStormRainClip, library, "storm_rain_loop_1");
            // 蓄力循环改用真正的无缝循环件：旧版指向一次性 wav，循环接缝会「咔」
            EnsureClip(ref loopChargerChargeClip, library, "charger_charge_loop_1");
        }

        /// <summary>
        /// 按族名回填整个变体数组。变体数由烘焙脚本决定，调用方不该写死名字，
        /// 否则一加变体，独立包里就只播前几个（旧做法的实际症状）。
        /// </summary>
        private static void EnsureVariantSet(ref AudioClip[] clips, RuntimeAssetLibrary library, string key)
        {
            if (clips != null && clips.Length > 0)
                return;

            AudioClip[] resolved = library.GetSfxVariants(key);
            if (resolved != null && resolved.Length > 0)
                clips = resolved;
        }

        private static void EnsureClip(ref AudioClip clip, RuntimeAssetLibrary library, string name)
        {
            if (clip == null)
                clip = library.GetSfx(name);
        }

        private static void EnsureClips(ref AudioClip[] clips, RuntimeAssetLibrary library, params string[] names)
        {
            if (clips != null && clips.Length > 0)
                return;

            var resolved = new List<AudioClip>(names.Length);
            for (int i = 0; i < names.Length; i++)
            {
                AudioClip clip = library.GetSfx(names[i]);
                if (clip != null)
                    resolved.Add(clip);
            }

            if (resolved.Count > 0)
                clips = resolved.ToArray();
        }

#if UNITY_EDITOR
private void EnsureEditorClips()
        {
            // 循环音效：优先取真正的 *_loop_* 件（蓄力循环也一样）
            if (loopRollerClip == null) loopRollerClip = LoadClip("roll_loop_1");
            if (loopSplatlingSpinClip == null) loopSplatlingSpinClip = LoadClip("splatling_spin_loop_1");
            if (loopSwimClip == null) loopSwimClip = LoadClip("swim_loop_1");
            if (loopClimbClip == null) loopClimbClip = LoadClip("climb_loop_1");
            if (loopEnemyInkSizzleClip == null) loopEnemyInkSizzleClip = LoadClip("enemy_ink_sizzle_loop_1");
            if (loopStormRainClip == null) loopStormRainClip = LoadClip("storm_rain_loop_1");
            if (loopChargerChargeClip == null) loopChargerChargeClip = LoadClip("charger_charge_loop_1");

            // 变体数组按族自动取全（旧版只填了前 2~3 个，新增变体在编辑器里也听不到）
            EnsureEditorVariants(ref shotClips, "shot");
            EnsureEditorVariants(ref inkHitGroundClips, "ink_hit_ground");
            EnsureEditorVariants(ref splatSmallClips, "splat_small");
            EnsureEditorVariants(ref splatBigClips, "splat_big");
            EnsureEditorVariants(ref inkHitWallClips, "ink_hit_wall");
            EnsureEditorVariants(ref hitEnemyClips, "hit_enemy");
            EnsureEditorVariants(ref rollerFlickClips, "roller_flick");
            EnsureEditorVariants(ref blasterShotClips, "blaster_shot");
            EnsureEditorVariants(ref blasterPumpClips, "blaster_pump");
            EnsureEditorVariants(ref blasterBoomClips, "blaster_boom");
            EnsureEditorVariants(ref sloshLandClips, "slosh_land");
            EnsureEditorVariants(ref swimSplashClips, "swim_splash");
            EnsureEditorVariants(ref stepDryClips, "step_dry");
            EnsureEditorVariants(ref stepInkBubbleClips, "step_ink_bubble");
            EnsureEditorVariants(ref stepEnemyClips, "step_enemy");
            EnsureEditorVariants(ref hitMarkerClips, "hit_marker");
            EnsureEditorVariants(ref stormThunderClips, "storm_thunder");
            EnsureEditorVariants(ref reticleLockClips, "reticle_lock");

            // 单发音效：旧版这里漏了 shot/ink_hit/hit_enemy/ink_empty/ink_refill/
            // kill/death/respawn/ult_ready/ult_cast/ui_click，场景没序列化时编辑器里就是哑的。
            if (inkEmptyClip == null) inkEmptyClip = LoadClip("ink_empty_1");
            if (inkRefillClip == null) inkRefillClip = LoadClip("ink_refill_1");
            if (chargerChargeClip == null) chargerChargeClip = LoadClip("charger_charge_1");
            if (chargerFullClip == null) chargerFullClip = LoadClip("charger_full_1");
            if (chargerShotClip == null) chargerShotClip = LoadClip("charger_shot_1");
            if (sloshThrowClip == null) sloshThrowClip = LoadClip("slosh_throw_1");
            if (splatlingReadyClip == null) splatlingReadyClip = LoadClip("splatling_ready_1");

            if (squidInClip == null) squidInClip = LoadClip("squid_in_1");
            if (squidOutClip == null) squidOutClip = LoadClip("squid_out_1");
            if (jumpClip == null) jumpClip = LoadClip("jump_1");
            if (landClip == null) landClip = LoadClip("land_1");
            if (dodgeRollClip == null) dodgeRollClip = LoadClip("dodge_roll_1");

            if (hurtClip == null) hurtClip = LoadClip("hurt_1");
            if (refillFullClip == null) refillFullClip = LoadClip("refill_full_1");
            if (spawnProtectClip == null) spawnProtectClip = LoadClip("spawn_protect_1");
            if (lowHealthClip == null) lowHealthClip = LoadClip("low_health_1");
            if (respawnTickClip == null) respawnTickClip = LoadClip("respawn_tick_1");

            if (bombThrowClip == null) bombThrowClip = LoadClip("bomb_throw_1");
            if (bombBeepClip == null) bombBeepClip = LoadClip("bomb_beep_1");
            if (bombExplodeClip == null) bombExplodeClip = LoadClip("bomb_explode_1");
            if (bombBounceClip == null) bombBounceClip = LoadClip("bomb_bounce_1");
            if (ultReadyClip == null) ultReadyClip = LoadClip("ult_ready_1");
            if (ultCastClip == null) ultCastClip = LoadClip("ult_cast_1");
            if (specialSlamClip == null) specialSlamClip = LoadClip("special_slam_1");
            if (stormWindClip == null) stormWindClip = LoadClip("storm_wind_1");

            if (killClip == null) killClip = LoadClip("kill_1");
            if (deathClip == null) deathClip = LoadClip("death_1");
            if (respawnClip == null) respawnClip = LoadClip("respawn_1");

            // 比赛流程提示音：场景引用缺省时按文件名兜底（编辑器内），保证无语音版本仍能出声
            if (countdownTickClip == null) countdownTickClip = LoadClip("countdown_tick_1");
            if (matchStartClip == null) matchStartClip = LoadClip("match_start_1");
            if (goCueClip == null) goCueClip = LoadClip("go_horn_1");
            if (oneMinuteCueClip == null) oneMinuteCueClip = LoadClip("one_minute_1");
            if (readyClip == null) readyClip = LoadClip("ready_1");
            if (matchEndClip == null) matchEndClip = LoadClip("match_end_1");
            if (timesUpClip == null) timesUpClip = LoadClip("times_up_1");
            if (resultWinClip == null) resultWinClip = LoadClip("result_win_1");
            if (resultLoseClip == null) resultLoseClip = LoadClip("result_lose_1");
            if (final10sClip == null) final10sClip = LoadClip("final_10s_1");
            if (leadGainClip == null) leadGainClip = LoadClip("lead_gain_1");
            if (leadLoseClip == null) leadLoseClip = LoadClip("lead_lose_1");
            if (enemySpecialWarnClip == null) enemySpecialWarnClip = LoadClip("enemy_special_warn_1");

            if (uiClickClip == null) uiClickClip = LoadClip("ui_click_1");
            if (uiHoverClip == null) uiHoverClip = LoadClip("ui_hover_1");
            if (uiConfirmClip == null) uiConfirmClip = LoadClip("ui_confirm_1");
            if (uiToggleClip == null) uiToggleClip = LoadClip("ui_toggle_1");
            if (uiBackClip == null) uiBackClip = LoadClip("ui_back_1");
            if (uiSliderClip == null) uiSliderClip = LoadClip("ui_slider_1");
            if (uiErrorClip == null) uiErrorClip = LoadClip("ui_error_1");
            if (lowInkWarningClip == null) lowInkWarningClip = LoadClip("low_ink_warning_1");
            if (pauseOpenClip == null) pauseOpenClip = LoadClip("pause_open_1");
            if (pauseCloseClip == null) pauseCloseClip = LoadClip("pause_close_1");
            if (weaponSwitchClip == null) weaponSwitchClip = LoadClip("weapon_switch_1");
            if (chargeCancelClip == null) chargeCancelClip = LoadClip("charge_cancel_1");
        }

        /// <summary>编辑器兜底：按族名探测 &lt;key&gt;_1.._64 直到缺号，取全变体。</summary>
        private static void EnsureEditorVariants(ref AudioClip[] clips, string key)
        {
            if (clips != null && clips.Length > 0)
                return;

            var list = new System.Collections.Generic.List<AudioClip>();
            for (int i = 1; i <= 64; i++)
            {
                AudioClip clip = LoadClip(key + "_" + i);
                if (clip == null)
                    break;
                list.Add(clip);
            }

            if (list.Count > 0)
                clips = list.ToArray();
        }

        private static AudioClip LoadClip(string name)
        {
            return UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/_Project/Audio/SFX/{name}.wav");
        }

        private static AudioClip[] LoadClips(params string[] names)
        {
            var list = new System.Collections.Generic.List<AudioClip>();
            foreach (var n in names)
            {
                var c = LoadClip(n);
                if (c != null) list.Add(c);
            }
            return list.ToArray();
        }
#endif
    }
}
