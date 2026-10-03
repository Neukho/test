using System;

namespace MorpheHouse.GameFlow
{
    /// <summary>믹서 채널. Master 는 나머지 전부에 곱해진다.</summary>
    public enum AudioChannel
    {
        Master = 0,
        Bgm = 1,
        Se = 2,
        Voice = 3
    }

    /// <summary>
    /// 오디오 믹서의 순수 계산: 채널별 슬라이더 값(0..1) → 지각 곡선 → 진폭/dB, 음소거, 목소리 구간 BGM 덕킹, BGM 교차 페이드 이득.
    /// Unity 의 AudioMixer 에셋 없이 AudioSource.volume 에 곱할 최종 진폭(<see cref="Gain"/>)만 돌려준다. Unity 에 의존하지 않는다.
    /// 슬라이더 곡선은 진폭 = 값², 즉 0.5 → -12 dB, 0.25 → -24 dB 로 사람 귀에 슬라이더가 고르게 느껴지게 한다(0 은 완전 무음).
    /// </summary>
    public sealed class AudioMixerModel
    {
        /// <summary>무음으로 취급하는 dB 바닥.</summary>
        public const float SilenceDb = -80f;

        /// <summary>목소리가 나오는 동안 BGM 에 적용하는 기본 감쇠(dB).</summary>
        public const float DefaultDuckDb = -9f;

        public const float DefaultMaster = 1f;
        public const float DefaultBgm = 0.8f;
        public const float DefaultSe = 0.9f;
        public const float DefaultVoice = 1f;

        private readonly float[] _volume = { DefaultMaster, DefaultBgm, DefaultSe, DefaultVoice };
        private readonly bool[] _muted = new bool[4];
        private bool _duckRequested;
        private float _duckLevel;

        /// <summary>전체 음소거(앱이 백그라운드로 갈 때 등). 채널별 음소거와 별개.</summary>
        public bool Muted;

        /// <summary>덕킹 감쇠량(dB, 0 이하). 0 이면 덕킹 없음.</summary>
        public float DuckDb = DefaultDuckDb;

        /// <summary>덕킹이 걸리는 데 걸리는 시간(초).</summary>
        public float DuckAttackSeconds = 0.15f;

        /// <summary>덕킹이 풀리는 데 걸리는 시간(초).</summary>
        public float DuckReleaseSeconds = 0.6f;

        // ---------------- 곡선 ----------------

        /// <summary>슬라이더 값(0..1, 범위 밖은 잘라냄, NaN 은 0) → 진폭(0..1). 값².</summary>
        public static float SliderToAmplitude(float slider)
        {
            float v = Clamp01(slider);
            return v * v;
        }

        /// <summary>진폭 → dB. 0.0001(-80 dB) 이하는 <see cref="SilenceDb"/>.</summary>
        public static float AmplitudeToDb(float amplitude)
        {
            if (float.IsNaN(amplitude) || amplitude <= 0.0001f) return SilenceDb;
            return (float)(20.0 * Math.Log10(amplitude));
        }

        /// <summary>dB → 진폭. <see cref="SilenceDb"/> 이하는 0, 0 dB 이상은 1.</summary>
        public static float DbToAmplitude(float db)
        {
            if (float.IsNaN(db) || db <= SilenceDb) return 0f;
            if (db >= 0f) return 1f;
            return (float)Math.Pow(10.0, db / 20.0);
        }

        /// <summary>슬라이더 값 → dB(AudioMixer 의 노출 파라미터에 넣을 값).</summary>
        public static float SliderToDb(float slider)
        {
            return AmplitudeToDb(SliderToAmplitude(slider));
        }

        /// <summary>dB → 슬라이더 값(SliderToDb 의 역함수).</summary>
        public static float DbToSlider(float db)
        {
            return (float)Math.Sqrt(DbToAmplitude(db));
        }

        /// <summary>
        /// 교차 페이드 이득(등전력): t 가 0 → 1 로 갈 때 나가는 쪽 cos, 들어오는 쪽 sin. t 는 0..1 로 잘라낸다.
        /// 합쳐도 소리 크기가 꺼지지 않는다(outGain² + inGain² = 1).
        /// </summary>
        public static void CrossfadeGains(float t, out float outGain, out float inGain)
        {
            float x = Clamp01(t);
            outGain = (float)Math.Cos(x * Math.PI / 2.0);
            inGain = (float)Math.Sin(x * Math.PI / 2.0);
        }

        // ---------------- 채널 값 ----------------

        public float Volume(AudioChannel channel)
        {
            return _volume[(int)channel];
        }

        public void SetVolume(AudioChannel channel, float slider)
        {
            _volume[(int)channel] = Clamp01(slider);
        }

        public bool IsChannelMuted(AudioChannel channel)
        {
            return _muted[(int)channel];
        }

        public void SetChannelMuted(AudioChannel channel, bool muted)
        {
            _muted[(int)channel] = muted;
        }

        /// <summary>설정의 볼륨 값(마스터·BGM·효과음·목소리)을 그대로 가져온다.</summary>
        public void SyncFrom(SettingsService settings)
        {
            if (settings == null) return;
            SetVolume(AudioChannel.Master, settings.MasterVolume);
            SetVolume(AudioChannel.Bgm, settings.BgmVolume);
            SetVolume(AudioChannel.Se, settings.SeVolume);
            SetVolume(AudioChannel.Voice, settings.VoiceVolume);
        }

        /// <summary>
        /// 채널의 최종 진폭(0..1) = 마스터 × 채널(곡선 적용) × (BGM 이면 덕킹). 전체/채널/마스터 음소거면 0.
        /// 마스터 채널을 물으면 마스터 자신만.
        /// </summary>
        public float Gain(AudioChannel channel)
        {
            if (Muted || _muted[(int)AudioChannel.Master]) return 0f;
            float master = SliderToAmplitude(_volume[(int)AudioChannel.Master]);
            if (channel == AudioChannel.Master) return master;
            if (_muted[(int)channel]) return 0f;
            float g = master * SliderToAmplitude(_volume[(int)channel]);
            if (channel == AudioChannel.Bgm && _duckLevel > 0f) g *= DbToAmplitude(DuckDb * _duckLevel);
            return g;
        }

        /// <summary>채널 최종 이득을 dB 로(무음이면 <see cref="SilenceDb"/>).</summary>
        public float GainDb(AudioChannel channel)
        {
            return AmplitudeToDb(Gain(channel));
        }

        // ---------------- 덕킹 ----------------

        /// <summary>목소리 구간 시작/끝. true 면 <see cref="Tick"/> 이 BGM 을 DuckDb 까지 서서히 줄인다.</summary>
        public void SetDucking(bool on)
        {
            _duckRequested = on;
        }

        public bool DuckingRequested { get { return _duckRequested; } }

        /// <summary>현재 덕킹 진행도(0 = 없음, 1 = 최대).</summary>
        public float DuckLevel { get { return _duckLevel; } }

        /// <summary>덕킹을 시간에 따라 진행시킨다(매 프레임).</summary>
        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f) return;
            if (_duckRequested)
            {
                float step = DuckAttackSeconds <= 0f ? 1f : deltaSeconds / DuckAttackSeconds;
                _duckLevel = Math.Min(1f, _duckLevel + step);
            }
            else
            {
                float step = DuckReleaseSeconds <= 0f ? 1f : deltaSeconds / DuckReleaseSeconds;
                _duckLevel = Math.Max(0f, _duckLevel - step);
            }
        }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v)) return 0f;
            return Math.Max(0f, Math.Min(1f, v));
        }
    }

    /// <summary>
    /// 같은 소리를 너무 자주 내지 않게 하는 간격 제한(타자기 틱·연타 클릭). 시간은 호출자가 넘기는 초 단위 단조 증가 값.
    /// </summary>
    public sealed class AudioThrottle
    {
        private readonly System.Collections.Generic.Dictionary<string, float> _last =
            new System.Collections.Generic.Dictionary<string, float>();

        /// <summary>id 별 최소 간격(초). 기본 0.05.</summary>
        public float MinInterval = 0.05f;

        /// <summary>지금 소리를 내도 되면 true 를 돌려주고 시각을 기록한다.</summary>
        public bool TryPass(string id, float now)
        {
            return TryPass(id, now, MinInterval);
        }

        /// <summary>간격을 직접 지정하는 버전.</summary>
        public bool TryPass(string id, float now, float minInterval)
        {
            if (string.IsNullOrEmpty(id)) return false;
            float last;
            if (_last.TryGetValue(id, out last) && now >= last && now - last < minInterval) return false;
            _last[id] = now;
            return true;
        }

        public void Reset()
        {
            _last.Clear();
        }
    }
}
