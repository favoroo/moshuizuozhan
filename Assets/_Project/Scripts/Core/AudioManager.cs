using System.Collections;
using UnityEngine;

namespace P1.Core
{
    /// <summary>
    /// 轻量音效管理器：管理 AudioSource 对象池，支持随机音高微调与高频音效节流。
    /// P5-C 扩展：播报员语音通道、双段动态 BGM（主阶段/决战段）、BGM Ducking 闪避。
    /// 原有 SFX 公共接口保持不变（PlayShot / PlayOne / PlayKill ...）。
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

        [Header("大招与击杀音效")]
        public AudioClip ultReadyClip;
        public AudioClip ultCastClip;
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

        private AudioSource[] _pool;
        private int _poolCursor;
        private float _lastGroundHitTime;
        private float _lastEmptyClickTime;
        private float _lastRefillTickTime;

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
            BuildPool(12);
            BuildMusicSources();
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
                    break;

                case MatchManager.Phase.Playing:
                    StartMatchMusic();
                    PlayAnnouncement(voiceGoClip, Mix.announcementDuckAmount);
                    break;

                case MatchManager.Phase.Finished:
                    PlayAnnouncement(voiceFinishClip, Mix.announcementDuckAmount);
                    FadeOutMusic(Mix.bgmFadeOutSeconds);
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

        /// <summary>
        /// 让 BGM 立刻下压 amount（0.55 = 压到 45%），holdSeconds 后平滑恢复。
        /// 重叠调用时取最深目标与最晚结束时间（与 Inkwave audio.js 的 duck 语义一致）。
        /// </summary>
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

        /// <summary>
        /// duckMusic=false 用于 Bot 大招：仍然播放音效，但不压玩家 BGM。
        /// </summary>
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

            // 只有本地玩家阵亡才压 BGM；队友 Bot 阵亡不打断玩家音乐。
            var player = P1.Player.PlayerController.LocalPlayer;
            if (player != null && player.Health != null && !player.Health.IsAlive)
                Duck(Mix.deathDuckAmount, 0.9f);
        }

        public void PlayRespawn() => PlayOne(respawnClip, 0.60f, 0.98f, 1.04f);

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
    }
}