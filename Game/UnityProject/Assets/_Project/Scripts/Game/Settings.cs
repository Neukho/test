using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace MorpheHouse.GameFlow
{
    /// <summary>설정 파일 저장소 추상화(settings.json 의 읽기/쓰기). 실패는 예외 대신 null / false.</summary>
    public interface ISettingsStorage
    {
        /// <summary>저장된 JSON 텍스트. 없으면 null.</summary>
        string Read();

        bool Write(string text);
    }

    /// <summary>메모리 저장소(테스트·폴백).</summary>
    public sealed class MemorySettingsStorage : ISettingsStorage
    {
        public string Text;
        public int WriteCount;

        public string Read()
        {
            return Text;
        }

        public bool Write(string text)
        {
            Text = text;
            WriteCount++;
            return true;
        }
    }

    /// <summary>파일 저장소(보통 persistentDataPath/settings.json).</summary>
    public sealed class FileSettingsStorage : ISettingsStorage, ICorruptPreserver
    {
        public const long MaxFileBytes = 1024 * 1024;
        private readonly string _path;

        public FileSettingsStorage(string path)
        {
            _path = path;
        }

        public string Read()
        {
            try
            {
                if (!File.Exists(_path)) return null;
                // 크기 상한(1MB) 초과는 손상으로 취급: 파싱에 실패하는 고정 문자열을 돌려준다(읽지 않음).
                if (new FileInfo(_path).Length > MaxFileBytes) return "<file too large>";
                return File.ReadAllText(_path, Encoding.UTF8);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        public bool Write(string text)
        {
            try
            {
                string dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, text, new UTF8Encoding(false));
                if (File.Exists(_path))
                {
                    // 원자적 교체(직전 파일은 .bak). 실패 시에만 수동 순서로 폴백.
                    try { File.Replace(tmp, _path, _path + ".bak"); return true; }
                    catch (IOException) { if (!File.Exists(tmp)) throw; }
                    catch (PlatformNotSupportedException) { }
                    File.Delete(_path);
                }
                File.Move(tmp, _path);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>손상으로 판정된 파일을 &lt;name&gt;.corrupt 로 보존한다(다음 저장이 덮어쓰기 전).</summary>
        public void PreserveCorrupt()
        {
            try
            {
                if (!File.Exists(_path)) return;
                string dest = _path + ".corrupt";
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(_path, dest);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// 사용자 설정(텍스트 속도·자동 진행 간격·볼륨(마스터/BGM/효과음/목소리)·언어·UI 크기·전체 화면). Unity 에 의존하지 않는다.
    /// TextSpeed 는 초당 글자 수이며 MaxTextSpeed(= Typewriter.InstantThreshold) 이상이면 즉시 표시.
    /// 저장 파일(settings.json)에 없는 키는 기본값을 쓰므로, 옛 파일도 그대로 읽힌다.
    /// </summary>
    public sealed class SettingsService
    {
        public const float MinTextSpeed = 10f;
        public const float MaxTextSpeed = 130f;
        public const float DefaultTextSpeed = 40f;

        /// <summary>자동 진행에서 한 줄을 다 보여 준 뒤 다음 줄로 넘어가기까지의 대기 시간(초) 범위.</summary>
        public const float MinAutoDelay = 0.4f;
        public const float MaxAutoDelay = 3.0f;
        public const float DefaultAutoDelay = 1.2f;

        /// <summary>선택할 수 있는 UI 크기 배율(CanvasScaler 기준 해상도를 이 값으로 나눠 키우거나 줄인다).</summary>
        public static readonly float[] UiScales = { 0.9f, 1.0f, 1.1f };

        private readonly ISettingsStorage _storage;
        private float _textSpeed = DefaultTextSpeed;
        private float _masterVolume = 1f;
        private float _bgmVolume = AudioMixerModel.DefaultBgm;
        private float _seVolume = AudioMixerModel.DefaultSe;
        private float _voiceVolume = AudioMixerModel.DefaultVoice;
        private float _autoDelay = DefaultAutoDelay;
        private float _uiScale = 1f;
        private bool _fullscreen = true;
        private string _locale = "ko";

        /// <summary>값이 바뀔 때마다 호출(볼륨 적용 등).</summary>
        public event Action Changed;

        public SettingsService(ISettingsStorage storage)
        {
            _storage = storage;
        }

        public float TextSpeed { get { return _textSpeed; } }

        public float MasterVolume { get { return _masterVolume; } }

        public float BgmVolume { get { return _bgmVolume; } }

        public float SeVolume { get { return _seVolume; } }

        public float VoiceVolume { get { return _voiceVolume; } }

        /// <summary>자동 진행 대기 시간(초). 작을수록 빠르다.</summary>
        public float AutoDelay { get { return _autoDelay; } }

        /// <summary>UI 크기 배율(0.9 / 1.0 / 1.1 중 하나).</summary>
        public float UiScale { get { return _uiScale; } }

        /// <summary>데스크톱 전체 화면 여부(모바일에서는 무시).</summary>
        public bool Fullscreen { get { return _fullscreen; } }

        /// <summary>"ko" 또는 "en".</summary>
        public string Locale { get { return _locale; } }

        public bool InstantText { get { return _textSpeed >= MaxTextSpeed; } }

        public void SetTextSpeed(float value)
        {
            float v = Clamp(value, MinTextSpeed, MaxTextSpeed);
            if (v == _textSpeed) return;
            _textSpeed = v;
            RaiseChanged();
        }

        public void SetMasterVolume(float value)
        {
            float v = Clamp(value, 0f, 1f);
            if (v == _masterVolume) return;
            _masterVolume = v;
            RaiseChanged();
        }

        public void SetBgmVolume(float value)
        {
            float v = Clamp(value, 0f, 1f);
            if (v == _bgmVolume) return;
            _bgmVolume = v;
            RaiseChanged();
        }

        public void SetSeVolume(float value)
        {
            float v = Clamp(value, 0f, 1f);
            if (v == _seVolume) return;
            _seVolume = v;
            RaiseChanged();
        }

        public void SetVoiceVolume(float value)
        {
            float v = Clamp(value, 0f, 1f);
            if (v == _voiceVolume) return;
            _voiceVolume = v;
            RaiseChanged();
        }

        public void SetAutoDelay(float seconds)
        {
            float v = Clamp(seconds, MinAutoDelay, MaxAutoDelay);
            if (v == _autoDelay) return;
            _autoDelay = v;
            RaiseChanged();
        }

        /// <summary>UI 배율을 정한다. 허용 값(0.9/1.0/1.1) 중 가장 가까운 값으로 맞춘다.</summary>
        public void SetUiScale(float value)
        {
            float v = SnapUiScale(value);
            if (v == _uiScale) return;
            _uiScale = v;
            RaiseChanged();
        }

        public void SetFullscreen(bool on)
        {
            if (on == _fullscreen) return;
            _fullscreen = on;
            RaiseChanged();
        }

        /// <summary>임의의 값을 허용된 UI 배율 중 가장 가까운 것으로(NaN 은 1.0).</summary>
        public static float SnapUiScale(float value)
        {
            if (float.IsNaN(value)) return 1f;
            float best = UiScales[0];
            for (int i = 1; i < UiScales.Length; i++)
                if (Math.Abs(UiScales[i] - value) < Math.Abs(best - value)) best = UiScales[i];
            return best;
        }

        public void SetLocale(string locale)
        {
            string l = (locale ?? string.Empty).Trim().ToLowerInvariant();
            l = l.StartsWith("en") ? "en" : "ko";
            if (l == _locale) return;
            _locale = l;
            RaiseChanged();
        }

        /// <summary>저장소에서 읽는다. 없거나 깨졌으면 기본값을 유지하고 false. 일부 키만 깨져도 나머지는 적용한다.</summary>
        public bool Load()
        {
            string text = _storage == null ? null : _storage.Read();
            if (string.IsNullOrEmpty(text)) return false;
            bool any = false;
            float f;
            if (TryReadNumber(text, "textSpeed", out f)) { _textSpeed = Clamp(f, MinTextSpeed, MaxTextSpeed); any = true; }
            if (TryReadNumber(text, "masterVolume", out f)) { _masterVolume = Clamp(f, 0f, 1f); any = true; }
            if (TryReadNumber(text, "bgmVolume", out f)) { _bgmVolume = Clamp(f, 0f, 1f); any = true; }
            if (TryReadNumber(text, "seVolume", out f)) { _seVolume = Clamp(f, 0f, 1f); any = true; }
            if (TryReadNumber(text, "voiceVolume", out f)) { _voiceVolume = Clamp(f, 0f, 1f); any = true; }
            if (TryReadNumber(text, "autoDelay", out f)) { _autoDelay = Clamp(f, MinAutoDelay, MaxAutoDelay); any = true; }
            if (TryReadNumber(text, "uiScale", out f)) { _uiScale = SnapUiScale(f); any = true; }
            bool b;
            if (TryReadBool(text, "fullscreen", out b)) { _fullscreen = b; any = true; }
            string s;
            if (TryReadString(text, "locale", out s)) { _locale = s.ToLowerInvariant().StartsWith("en") ? "en" : "ko"; any = true; }
            if (!any)
            {
                // 아무 키도 읽지 못한 비어 있지 않은 파일은 손상으로 보고 덮어쓰기 전에 보존한다.
                ICorruptPreserver keep = _storage as ICorruptPreserver;
                if (keep != null) keep.PreserveCorrupt();
            }
            if (any) RaiseChanged();
            return any;
        }

        public bool Save()
        {
            return _storage != null && _storage.Write(ToJson());
        }

        public string ToJson()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"textSpeed\": ").Append(_textSpeed.ToString("0.###", CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"masterVolume\": ").Append(_masterVolume.ToString("0.###", CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"bgmVolume\": ").Append(_bgmVolume.ToString("0.###", CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"seVolume\": ").Append(_seVolume.ToString("0.###", CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"voiceVolume\": ").Append(_voiceVolume.ToString("0.###", CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"autoDelay\": ").Append(_autoDelay.ToString("0.###", CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"uiScale\": ").Append(_uiScale.ToString("0.###", CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"fullscreen\": ").Append(_fullscreen ? "true" : "false").Append(",\n");
            sb.Append("  \"locale\": \"").Append(_locale).Append("\"\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        private void RaiseChanged()
        {
            Action h = Changed;
            if (h != null) h();
        }

        private static float Clamp(float v, float min, float max)
        {
            if (float.IsNaN(v)) return min;
            return Math.Max(min, Math.Min(max, v));
        }

        private static int ValueStart(string json, string key)
        {
            int k = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (k < 0) return -1;
            int colon = json.IndexOf(':', k + key.Length + 2);
            if (colon < 0) return -1;
            int i = colon + 1;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            return i;
        }

        private static bool TryReadNumber(string json, string key, out float value)
        {
            value = 0f;
            int i = ValueStart(json, key);
            if (i < 0) return false;
            int j = i;
            while (j < json.Length && (char.IsDigit(json[j]) || json[j] == '.' || json[j] == '-' || json[j] == '+' || json[j] == 'e' || json[j] == 'E')) j++;
            if (j == i) return false;
            return float.TryParse(json.Substring(i, j - i), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryReadBool(string json, string key, out bool value)
        {
            value = false;
            int i = ValueStart(json, key);
            if (i < 0) return false;
            if (string.CompareOrdinal(json, i, "true", 0, 4) == 0) { value = true; return true; }
            if (string.CompareOrdinal(json, i, "false", 0, 5) == 0) { value = false; return true; }
            return false;
        }

        private static bool TryReadString(string json, string key, out string value)
        {
            value = null;
            int i = ValueStart(json, key);
            if (i < 0 || i >= json.Length || json[i] != '"') return false;
            int end = json.IndexOf('"', i + 1);
            if (end < 0) return false;
            value = json.Substring(i + 1, end - i - 1);
            return true;
        }
    }
}
