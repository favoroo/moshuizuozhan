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
    /// 播报员语音通道、双段动态 BGM（主阶段/决战段）、BGM Ducking 闪避以及独立循环音通道。
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

        [Header("播报员语音（P5-C）")]
        public AudioClip voiceCount3Clip;
        public AudioClip voiceCount2Clip;
        public AudioClip voiceCount1Clip;
        public AudioClip voiceGoClip;
        public AudioClip voiceOneMinuteClip;
        public AudioClip voiceFinishClip;

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

        private AudioSource _musicMainSource;
        private AudioSource _musicFinalSource;
        private AudioSource _voiceSource;
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
            EnsureRuntimeClips();
#if UNITY_EDITOR
            EnsureEditorClips();
#endif
            BuildPool(12);
            BuildMusicSources();
            BuildLoopSources();
        }

        private void Start()
        {
            TryHookMatch();
        }

        private void OnDestroy()
        {
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
                _pool[i] = src;
            }
        }

        private void BuildMusicSources()
        {
            _musicMainSource = CreatePersistentSource("BGM_Main", true, 96);
            _musicFinalSource = CreatePersistentSource("BGM_Final", true, 96);
            _voiceSource = CreatePersistentSource("AnnouncerVoice", false, 0);
        }

        private void BuildLoopSources()
        {
            var kinds = (LoopSfxKind[])System.Enum.GetValues(typeof(LoopSfxKind));
            for (int i = 0; i < kinds.Length; i++)
            {
                LoopSfxKind kind = kinds[i];
                var src = CreatePersistentSource($"LoopVoice_{kind}", true, 80);
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
                case MatchManager.Phase.Countdown:
                    ResetMusicForNewMatch();
                    StopAllLoops();
                    break;

                case MatchManager.Phase.Playing:
                    StartMatchMusic();
                    PlayAnnouncement(voiceGoClip, Mix.announcementDuckAmount);
                    break;

                case MatchManager.Phase.Finished:
                    PlayAnnouncement(voiceFinishClip, Mix.announcementDuckAmount);
                    FadeOutMusic(Mix.bgmFadeOutSeconds);
                    StopAllLoops();
                    break;
            }
        }

        private void OnCountdownTicked(int shown)
        {
            if (shown == 3)
                PlayAnnouncement(voiceCount3Clip, Mix.announcementDuckAmount);
            else if (shown == 2)
                PlayAnnouncement(voiceCount2Clip, Mix.announcementDuckAmount);
            else if (shown == 1)
                PlayAnnouncement(voiceCount1Clip, Mix.announcementDuckAmount);
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
            PlayAnnouncement(voiceOneMinuteClip, Mix.announcementDuckAmount);
            CrossfadeToFinalMusic();
        }

        // ---------------------------------------------------------------- 动态 BGM

        private void ResetMusicForNewMatch()
        {
            StopMusicRoutine();
            StopSource(_musicMainSource);
            StopSource(_musicFinalSource);
            StopSource(_voiceSource);
            _musicMainGain = 0f;
            _musicFinalGain = 0f;
            _duckFactor = 1f;
            _duckTarget = 1f;
            _duckHoldUntil = -1f;
            _matchMusicStarted = false;
            _finalStage = false;
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
            float volume = Mix.bgmVolume * _duckFactor;
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

        public void PlayAnnouncement(AudioClip clip, float duckAmount = 0.55f)
        {
            if (clip == null || _voiceSource == null)
                return;

            _voiceSource.volume = Mix.voiceVolume;
            _voiceSource.clip = clip;
            _voiceSource.Play();
            Duck(duckAmount, clip.length);
            LogAudio($"播报：{clip.name} ({clip.length:F2}s)");
        }

        // ---------------------------------------------------------------- 原有 SFX 接口（保持不变）

        public void PlayShot()
        {
            PlayRandom(shotClips, 0.42f, 0.94f, 1.08f);
        }

        public void PlayInkHitGround(float volumeScale = 1f)
        {
            if (Time.unscaledTime - _lastGroundHitTime < 0.045f)
                return;

            _lastGroundHitTime = Time.unscaledTime;
            PlayRandom(inkHitGroundClips, 0.36f * volumeScale, 0.90f, 1.12f);
        }

        public void PlayHitEnemy()
        {
            PlayRandom(hitEnemyClips, 0.65f, 0.95f, 1.05f);
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

        public void PlayDeath()
        {
            PlayOne(deathClip, 0.75f, 0.96f, 1.02f);

            var player = P1.Player.PlayerController.LocalPlayer;
            if (player != null && player.Health != null && !player.Health.IsAlive)
                Duck(Mix.deathDuckAmount, 0.9f);
        }

        public void PlayRespawn() => PlayOne(respawnClip, 0.60f, 0.98f, 1.04f);

        // ---------------------------------------------------------------- 新增 SFX 扩展接口

        /// <summary>按武器分类播放专属开火音，无专属音时优雅回落至 shotClips。</summary>
        public void PlayWeaponShot(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.Roller:
                    if (rollerFlickClips != null && rollerFlickClips.Length > 0)
                        PlayRandom(rollerFlickClips, 0.45f, 0.94f, 1.06f);
                    else
                        PlayShot();
                    break;

                case WeaponKind.Charger:
                    if (chargerShotClip != null)
                        PlayOne(chargerShotClip, 0.55f, 0.97f, 1.03f);
                    else
                        PlayShot();
                    break;

                case WeaponKind.Blaster:
                    if (blasterShotClips != null && blasterShotClips.Length > 0)
                        PlayRandom(blasterShotClips, 0.48f, 0.94f, 1.06f);
                    else
                        PlayShot();
                    break;

                case WeaponKind.Slosher:
                    if (sloshThrowClip != null)
                        PlayOne(sloshThrowClip, 0.45f, 0.95f, 1.05f);
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

        public void PlayBombThrow()
        {
            AudioClip clip = bombThrowClip != null ? bombThrowClip : (shotClips != null && shotClips.Length > 0 ? shotClips[0] : null);
            PlayOne(clip, 0.50f, 0.95f, 1.05f);
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

        public void PlayHurt()
        {
            if (Time.unscaledTime - _lastHurtTime < Mix.hurtThrottleSeconds)
                return;

            _lastHurtTime = Time.unscaledTime;
            PlayOne(hurtClip, 0.70f, 0.95f, 1.05f);
        }

        public void PlayJump()
        {
            PlayOne(jumpClip, 0.50f, 0.97f, 1.03f);
        }

        public void PlayLand()
        {
            PlayOne(landClip, 0.55f, 0.96f, 1.04f);
        }

        public void PlaySquidIn()
        {
            PlayOne(squidInClip, 0.60f, 0.98f, 1.02f);
        }

        public void PlaySquidOut()
        {
            PlayOne(squidOutClip, 0.60f, 0.98f, 1.02f);
        }

        public void PlayFootstep(bool onInk, bool onEnemyInk)
        {
            if (Time.unscaledTime - _lastFootstepTime < Mix.footstepThrottleSeconds)
                return;

            _lastFootstepTime = Time.unscaledTime;

            if (onEnemyInk)
                PlayRandom(stepEnemyClips, 0.40f, 0.95f, 1.05f);
            else if (onInk)
                PlayRandom(stepInkBubbleClips, 0.35f, 0.95f, 1.05f);
            else
                PlayRandom(stepDryClips, 0.30f, 0.92f, 1.08f);
        }

        public void PlaySwimSplash()
        {
            if (Time.unscaledTime - _lastSwimSplashTime < Mix.swimSplashThrottleSeconds)
                return;

            _lastSwimSplashTime = Time.unscaledTime;
            PlayRandom(swimSplashClips, 0.45f, 0.95f, 1.05f);
        }

        public void PlayHitMarker()
        {
            if (Time.unscaledTime - _lastHitMarkerTime < Mix.hitMarkerThrottleSeconds)
                return;

            _lastHitMarkerTime = Time.unscaledTime;
            PlayRandom(hitMarkerClips, 0.65f, 0.98f, 1.02f);
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

        // ---------------------------------------------------------------- 循环音效通道管线

        /// <summary>
        /// 启动指定类型的循环音效：带淡入包络，同一循环重复 Play 不会叠音或重置播放位置。
        /// </summary>
        public void PlayLoop(LoopSfxKind kind)
        {
            if (!isActiveAndEnabled)
                return;

            EnsureLoopSources();
            if (!_loopChannels.TryGetValue(kind, out LoopChannel channel) || channel.Source == null)
                return;

            AudioClip clip = GetLoopClip(kind);
            if (clip == null)
                return;

            if (channel.Source.isPlaying && channel.IsFadingIn)
                return;

            float baseVol = GetLoopBaseVolume(kind);
            float targetVol = baseVol * Mix.loopMasterVolume;

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

            if (channel.FadeRoutine != null)
            {
                StopCoroutine(channel.FadeRoutine);
                channel.FadeRoutine = null;
            }

            channel.IsFadingIn = false;
            channel.TargetVolume = 0f;
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
            src.volume = volume;
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

            EnsureClips(ref shotClips, library, "shot_1", "shot_2", "shot_3");
            EnsureClips(ref inkHitGroundClips, library, "ink_hit_ground_1", "ink_hit_ground_2", "ink_hit_ground_3");
            EnsureClips(ref hitEnemyClips, library, "hit_enemy_1", "hit_enemy_2");
            EnsureClip(ref inkEmptyClip, library, "ink_empty_1");
            EnsureClip(ref inkRefillClip, library, "ink_refill_1");

            EnsureClips(ref rollerFlickClips, library, "roller_flick_1", "roller_flick_2");
            EnsureClip(ref chargerChargeClip, library, "charger_charge_1");
            EnsureClip(ref chargerFullClip, library, "charger_full_1");
            EnsureClip(ref chargerShotClip, library, "charger_shot_1");
            EnsureClips(ref blasterShotClips, library, "blaster_shot_1", "blaster_shot_2");
            EnsureClips(ref blasterBoomClips, library, "blaster_boom_1", "blaster_boom_2");
            EnsureClip(ref sloshThrowClip, library, "slosh_throw_1");
            EnsureClips(ref sloshLandClips, library, "slosh_land_1", "slosh_land_2");

            EnsureClip(ref squidInClip, library, "squid_in_1");
            EnsureClip(ref squidOutClip, library, "squid_out_1");
            EnsureClips(ref swimSplashClips, library, "swim_splash_1", "swim_splash_2");
            EnsureClips(ref stepDryClips, library, "step_dry_1", "step_dry_2", "step_dry_3");
            EnsureClips(ref stepInkBubbleClips, library, "step_ink_bubble_1", "step_ink_bubble_2", "step_ink_bubble_3");
            EnsureClips(ref stepEnemyClips, library, "step_enemy_1", "step_enemy_2", "step_enemy_3");
            EnsureClip(ref jumpClip, library, "jump_1");
            EnsureClip(ref landClip, library, "land_1");

            EnsureClip(ref hurtClip, library, "hurt_1");
            EnsureClips(ref hitMarkerClips, library, "hit_marker_1", "hit_marker_2");
            EnsureClip(ref refillFullClip, library, "refill_full_1");

            EnsureClip(ref bombThrowClip, library, "bomb_throw_1");
            EnsureClip(ref bombBeepClip, library, "bomb_beep_1");
            EnsureClip(ref bombExplodeClip, library, "bomb_explode_1");
            EnsureClip(ref ultReadyClip, library, "ult_ready_1");
            EnsureClip(ref ultCastClip, library, "ult_cast_1");
            EnsureClip(ref specialSlamClip, library, "special_slam_1");

            EnsureClip(ref killClip, library, "kill_1");
            EnsureClip(ref deathClip, library, "death_1");
            EnsureClip(ref respawnClip, library, "respawn_1");

            EnsureClip(ref countdownTickClip, library, "countdown_tick_1");
            EnsureClip(ref matchStartClip, library, "match_start_1");
            EnsureClip(ref matchEndClip, library, "match_end_1");
            EnsureClip(ref resultWinClip, library, "result_win_1");
            EnsureClip(ref resultLoseClip, library, "result_lose_1");
            EnsureClip(ref uiClickClip, library, "ui_click_1");
            EnsureClip(ref uiHoverClip, library, "ui_hover_1");
            EnsureClip(ref uiConfirmClip, library, "ui_confirm_1");
            EnsureClip(ref uiToggleClip, library, "ui_toggle_1");
            EnsureClip(ref uiBackClip, library, "ui_back_1");
            EnsureClip(ref uiSliderClip, library, "ui_slider_1");
            EnsureClip(ref uiErrorClip, library, "ui_error_1");
            EnsureClip(ref lowInkWarningClip, library, "low_ink_warning_1");

            EnsureClip(ref loopRollerClip, library, "roll_loop_1");
            EnsureClip(ref loopSplatlingSpinClip, library, "splatling_spin_loop_1");
            EnsureClip(ref loopSwimClip, library, "swim_loop_1");
            EnsureClip(ref loopClimbClip, library, "climb_loop_1");
            EnsureClip(ref loopEnemyInkSizzleClip, library, "enemy_ink_sizzle_loop_1");
            EnsureClip(ref loopStormRainClip, library, "storm_rain_loop_1");
            EnsureClip(ref loopChargerChargeClip, library, "charger_charge_1");
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
            if (loopRollerClip == null) loopRollerClip = LoadClip("roll_loop_1");
            if (loopSplatlingSpinClip == null) loopSplatlingSpinClip = LoadClip("splatling_spin_loop_1");
            if (loopSwimClip == null) loopSwimClip = LoadClip("swim_loop_1");
            if (loopClimbClip == null) loopClimbClip = LoadClip("climb_loop_1");
            if (loopEnemyInkSizzleClip == null) loopEnemyInkSizzleClip = LoadClip("enemy_ink_sizzle_loop_1");
            if (loopStormRainClip == null) loopStormRainClip = LoadClip("storm_rain_loop_1");
            if (loopChargerChargeClip == null) loopChargerChargeClip = LoadClip("charger_charge_1");

            if (rollerFlickClips == null || rollerFlickClips.Length == 0)
                rollerFlickClips = LoadClips("roller_flick_1", "roller_flick_2");
            if (chargerChargeClip == null) chargerChargeClip = LoadClip("charger_charge_1");
            if (chargerFullClip == null) chargerFullClip = LoadClip("charger_full_1");
            if (chargerShotClip == null) chargerShotClip = LoadClip("charger_shot_1");
            if (blasterShotClips == null || blasterShotClips.Length == 0)
                blasterShotClips = LoadClips("blaster_shot_1", "blaster_shot_2");
            if (blasterBoomClips == null || blasterBoomClips.Length == 0)
                blasterBoomClips = LoadClips("blaster_boom_1", "blaster_boom_2");
            if (sloshThrowClip == null) sloshThrowClip = LoadClip("slosh_throw_1");
            if (sloshLandClips == null || sloshLandClips.Length == 0)
                sloshLandClips = LoadClips("slosh_land_1", "slosh_land_2");

            if (squidInClip == null) squidInClip = LoadClip("squid_in_1");
            if (squidOutClip == null) squidOutClip = LoadClip("squid_out_1");
            if (swimSplashClips == null || swimSplashClips.Length == 0)
                swimSplashClips = LoadClips("swim_splash_1", "swim_splash_2");
            if (stepDryClips == null || stepDryClips.Length == 0)
                stepDryClips = LoadClips("step_dry_1", "step_dry_2", "step_dry_3");
            if (stepInkBubbleClips == null || stepInkBubbleClips.Length == 0)
                stepInkBubbleClips = LoadClips("step_ink_bubble_1", "step_ink_bubble_2", "step_ink_bubble_3");
            if (stepEnemyClips == null || stepEnemyClips.Length == 0)
                stepEnemyClips = LoadClips("step_enemy_1", "step_enemy_2", "step_enemy_3");
            if (jumpClip == null) jumpClip = LoadClip("jump_1");
            if (landClip == null) landClip = LoadClip("land_1");

            if (hurtClip == null) hurtClip = LoadClip("hurt_1");
            if (hitMarkerClips == null || hitMarkerClips.Length == 0)
                hitMarkerClips = LoadClips("hit_marker_1", "hit_marker_2");
            if (refillFullClip == null) refillFullClip = LoadClip("refill_full_1");

            if (bombThrowClip == null) bombThrowClip = LoadClip("bomb_throw_1");
            if (bombBeepClip == null) bombBeepClip = LoadClip("bomb_beep_1");
            if (bombExplodeClip == null) bombExplodeClip = LoadClip("bomb_explode_1");
            if (specialSlamClip == null) specialSlamClip = LoadClip("special_slam_1");

            if (uiHoverClip == null) uiHoverClip = LoadClip("ui_hover_1");
            if (uiConfirmClip == null) uiConfirmClip = LoadClip("ui_confirm_1");
            if (uiToggleClip == null) uiToggleClip = LoadClip("ui_toggle_1");
            if (uiBackClip == null) uiBackClip = LoadClip("ui_back_1");
            if (uiSliderClip == null) uiSliderClip = LoadClip("ui_slider_1");
            if (uiErrorClip == null) uiErrorClip = LoadClip("ui_error_1");
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
