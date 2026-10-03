using System.Collections.Generic;
using MorpheHouse.GameFlow;
using UnityEngine;

namespace MorpheHouse.UI
{
    /// <summary>
    /// 오디오 서비스(MonoBehaviour, GameBootstrap 이 만든다): BGM 2채널 교차 페이드 + 풀링된 효과음 + 설정 볼륨 반영.
    /// 계산(곡선·덕킹·교차 페이드 이득·간격 제한)은 순수 클래스(AudioMixerModel, AudioThrottle)가 하고, 이 클래스는 AudioSource 에 값을 전달만 한다.
    /// 클립 파일이 없으면(지금은 하나도 없다) 한 번만 경고를 남기고 조용히 넘어간다 — 게임은 무음으로 정상 동작한다.
    /// 대사 커맨드 &lt;&lt;bgm&gt;&gt; &lt;&lt;bgm_stop&gt;&gt; &lt;&lt;se&gt;&gt; 의 받는 쪽(IAudioCommandSink)이기도 하다.
    /// </summary>
    public sealed class AudioService : MonoBehaviour, IAudioCommandSink
    {
        private const int SePoolSize = 8;
        private const float DefaultFadeSeconds = 1.0f;

        private readonly AudioMixerModel _mixer = new AudioMixerModel();
        private readonly AudioThrottle _throttle = new AudioThrottle();
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private readonly HashSet<string> _warned = new HashSet<string>();
        private readonly AudioSource[] _bgm = new AudioSource[2];
        private readonly float[] _bgmEntryVolume = new float[2];
        private AudioSource[] _se;
        private int _nextSe;
        private int _front;
        private string _bgmId;
        private float _fadeT = 1f;
        private float _fadeSeconds = DefaultFadeSeconds;
        private SettingsService _settings;
        private AudioCatalog _catalog = AudioCatalog.Parse(null);

        /// <summary>믹서 모델(테스트·디버그용).</summary>
        public AudioMixerModel Mixer { get { return _mixer; } }

        public AudioCatalog Catalog { get { return _catalog; } }

        /// <summary>지금 재생 요청된 BGM id(클립이 없어 실제로는 무음이어도 id 는 남는다). 없으면 null.</summary>
        public string CurrentBgm { get { return _bgmId; } }

        /// <summary>매니페스트를 읽고 AudioSource 를 만든다. 설정이 바뀌면 볼륨을 다시 계산한다.</summary>
        public void Init(SettingsService settings)
        {
            _settings = settings;
            TextAsset manifest = Resources.Load<TextAsset>(AudioCatalog.ManifestResourcePath);
            _catalog = AudioCatalog.Parse(manifest != null ? manifest.text : null);
            for (int i = 0; i < _catalog.Errors.Count; i++) Debug.LogWarning("[Audio] " + _catalog.Errors[i]);
            for (int i = 0; i < _catalog.Warnings.Count; i++) Debug.Log("[Audio] " + _catalog.Warnings[i]);

            for (int i = 0; i < _bgm.Length; i++) _bgm[i] = NewSource("Bgm" + i, true);
            _se = new AudioSource[SePoolSize];
            for (int i = 0; i < _se.Length; i++) _se[i] = NewSource("Se" + i, false);

            UiAudio.PlaySe = PlaySe;
            if (_settings != null) _settings.Changed += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_settings != null) _settings.Changed -= Refresh;
            if (UiAudio.PlaySe == (System.Action<string>)PlaySe) UiAudio.PlaySe = null;
        }

        private AudioSource NewSource(string name, bool loop)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            AudioSource s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            s.spatialBlend = 0f;
            return s;
        }

        /// <summary>설정(마스터/BGM/효과음/목소리)을 믹서에 가져온다.</summary>
        public void Refresh()
        {
            _mixer.SyncFrom(_settings);
            ApplyBgmVolumes(0f);
        }

        // ---------------- IAudioCommandSink ----------------

        public bool HasAudio(string id, AudioKind kind)
        {
            return _catalog.Has(id, kind);
        }

        public void PlayBgm(string id)
        {
            PlayBgm(id, DefaultFadeSeconds);
        }

        /// <summary>BGM 교차 페이드 재생. 같은 곡이 이미 요청돼 있으면 무시. 알 수 없는 id 는 무시(경고 한 번).</summary>
        public void PlayBgm(string id, float fadeSeconds)
        {
            AudioEntry e;
            if (!_catalog.TryGet(id, out e) || e.Kind != AudioKind.Bgm)
            {
                WarnOnce("bgm:" + id, "알 수 없는 BGM id '" + id + "' (무시)");
                return;
            }
            if (_bgmId == e.Id) return;
            _bgmId = e.Id;

            int old = _front;
            _front = 1 - _front;
            AudioClip clip = Load(e);
            AudioSource next = _bgm[_front];
            next.Stop();
            next.clip = clip;
            next.loop = e.Loop;
            next.volume = 0f;
            _bgmEntryVolume[_front] = e.DefaultVolume;
            if (clip != null) next.Play();
            _fadeSeconds = Mathf.Max(0.01f, fadeSeconds);
            _fadeT = 0f;
            ApplyBgmVolumes(0f);
        }

        public void StopBgm()
        {
            StopBgm(DefaultFadeSeconds);
        }

        /// <summary>BGM 을 페이드 아웃한다(지금 곡을 "나가는 쪽" 으로 밀고 앞 채널을 비운다).</summary>
        public void StopBgm(float fadeSeconds)
        {
            if (_bgmId == null) return;
            _bgmId = null;
            _front = 1 - _front;
            AudioSource empty = _bgm[_front];
            empty.Stop();
            empty.clip = null;
            _fadeSeconds = Mathf.Max(0.01f, fadeSeconds);
            _fadeT = 0f;
        }

        public void PlaySe(string id)
        {
            AudioEntry e;
            if (!_catalog.TryGet(id, out e) || e.Kind == AudioKind.Bgm)
            {
                WarnOnce("se:" + id, "알 수 없는 효과음 id '" + id + "' (무시)");
                return;
            }
            AudioClip clip = Load(e);
            if (clip == null || _se == null) return;
            AudioSource src = _se[_nextSe];
            _nextSe = (_nextSe + 1) % _se.Length;
            AudioChannel channel = e.Kind == AudioKind.Voice ? AudioChannel.Voice : AudioChannel.Se;
            src.PlayOneShot(clip, _mixer.Gain(channel) * e.DefaultVolume);
        }

        /// <summary>같은 id 를 minInterval 초 안에 다시 내지 않는다(타자기 틱 등).</summary>
        public void PlaySeThrottled(string id, float minInterval)
        {
            if (_throttle.TryPass(id, Time.unscaledTime, minInterval)) PlaySe(id);
        }

        /// <summary>목소리가 나오는 동안 BGM 을 낮춘다(목소리 에셋이 생기면 VN 이 호출. 지금은 호출하는 곳이 없다).</summary>
        public void SetVoiceActive(bool active)
        {
            _mixer.SetDucking(active);
        }

        // ---------------- 내부 ----------------

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _mixer.Tick(dt);
            ApplyBgmVolumes(dt);
        }

        private void ApplyBgmVolumes(float dt)
        {
            if (_bgm[0] == null) return;
            if (_fadeT < 1f && dt > 0f) _fadeT = Mathf.Min(1f, _fadeT + dt / _fadeSeconds);
            float outGain, inGain;
            AudioMixerModel.CrossfadeGains(_fadeT, out outGain, out inGain);
            float master = _mixer.Gain(AudioChannel.Bgm);

            int old = 1 - _front;
            AudioSource front = _bgm[_front];
            AudioSource back = _bgm[old];
            front.volume = master * _bgmEntryVolume[_front] * inGain;
            back.volume = master * _bgmEntryVolume[old] * outGain;
            if (_fadeT >= 1f && back.isPlaying)
            {
                back.Stop();
                back.clip = null; // 끝난 클립 참조를 놓아 스트리밍 버퍼가 풀리게 한다.
            }
        }

        private AudioClip Load(AudioEntry e)
        {
            AudioClip clip;
            if (_clips.TryGetValue(e.Id, out clip)) return clip;
            clip = Resources.Load<AudioClip>(e.Path);
            if (clip == null) WarnOnce("clip:" + e.Id, "클립 파일이 없어 무음으로 넘어갑니다: Resources/" + e.Path);
            _clips[e.Id] = clip;
            return clip;
        }

        private void WarnOnce(string key, string message)
        {
            if (_warned.Add(key)) Debug.LogWarning("[Audio] " + message);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            // 모바일에서는 백그라운드로 가면 소리를 끈다(데스크톱은 창 포커스와 무관하게 계속 재생).
            if (Application.isMobilePlatform) _mixer.Muted = !hasFocus;
        }
    }
}
