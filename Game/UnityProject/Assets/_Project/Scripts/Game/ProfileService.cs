using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MorpheHouse.Data;
using MorpheHouse.Romance;

namespace MorpheHouse.GameFlow
{
    /// <summary>손상으로 판정된 파일을 덮어쓰기 전에 보존하는 저장소(선택 구현). 프로필·설정이 Load 중 손상을 발견하면 호출한다.</summary>
    public interface ICorruptPreserver
    {
        void PreserveCorrupt();
    }

    /// <summary>프로필(profile.json) 저장소 추상화. 실패는 예외 대신 null / false(SettingsService 와 같은 패턴).</summary>
    public interface IProfileStorage
    {
        /// <summary>저장된 JSON 텍스트. 없으면 null.</summary>
        string Read();

        bool Write(string text);
    }

    /// <summary>메모리 저장소(테스트·폴백).</summary>
    public sealed class MemoryProfileStorage : IProfileStorage
    {
        public string Text;
        public int WriteCount;
        /// <summary>true 면 Write 가 실패(false)한다(저장 실패 시험용).</summary>
        public bool FailWrites;

        public string Read()
        {
            return Text;
        }

        public bool Write(string text)
        {
            if (FailWrites) return false;
            Text = text;
            WriteCount++;
            return true;
        }
    }

    /// <summary>파일 저장소(보통 persistentDataPath/profile.json). 임시 파일에 쓴 뒤 교체한다.</summary>
    public sealed class FileProfileStorage : IProfileStorage, ICorruptPreserver
    {
        public const long MaxFileBytes = 1024 * 1024;
        private readonly string _path;

        public FileProfileStorage(string path)
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

    /// <summary>프로필 불러오기 결과.</summary>
    public enum ProfileLoadStatus
    {
        /// <summary>저장 파일이 없음(첫 실행) — 빈 프로필.</summary>
        Missing = 0,
        /// <summary>정상 로드.</summary>
        Loaded = 1,
        /// <summary>깨진 파일 — 빈 프로필로 시작(다음 저장 때 덮어씀).</summary>
        Corrupt = 2
    }

    /// <summary>
    /// 플레이 간에 이어지는 프로필(순수): 도달한 엔딩(키 → 횟수), 본 CG id, 해금 id. Unity 에 의존하지 않는다.
    /// 파일이 없거나 깨졌어도 예외 없이 빈 프로필로 동작하며, 필드별로 잘못된 값은 무시한다.
    /// 엔딩 키: 단독 &lt;ending&gt;:&lt;char&gt;(예 good:howl), 공동 harem_duo / harem_trio, 커뮤니티 community.
    /// </summary>
    public sealed class ProfileService
    {
        public const int CurrentVersion = 1;
        private const int MaxEntries = 512;
        private const int MaxKeyLength = 64;

        private readonly IProfileStorage _storage;
        private readonly Dictionary<string, int> _endings = new Dictionary<string, int>();
        private readonly List<string> _cgs = new List<string>();
        private readonly List<string> _unlocks = new List<string>();

        public ProfileService(IProfileStorage storage)
        {
            _storage = storage;
        }

        public ProfileLoadStatus LastLoadStatus { get; private set; }

        /// <summary>도달한 서로 다른 엔딩 수.</summary>
        public int ReachedCount { get { return _endings.Count; } }

        public IList<string> SeenCgs { get { return _cgs; } }

        public IList<string> Unlocks { get { return _unlocks; } }

        /// <summary>엔딩 결과 → 프로필 키.</summary>
        public static string EndingKey(EndingResult result)
        {
            if (result == null) throw new ArgumentNullException("result");
            switch (result.Kind)
            {
                case EndingKind.Normal: return "normal:" + result.PrimaryChar;
                case EndingKind.Good: return "good:" + result.PrimaryChar;
                case EndingKind.True: return "true:" + result.PrimaryChar;
                case EndingKind.HaremDuo: return EndingTable.HaremDuo;
                case EndingKind.HaremTrio: return EndingTable.HaremTrio;
                default: return EndingTable.Community;
            }
        }

        public bool HasReached(string key)
        {
            return key != null && _endings.ContainsKey(key);
        }

        public int ReachCount(string key)
        {
            int n;
            return key != null && _endings.TryGetValue(key, out n) ? n : 0;
        }

        /// <summary>엔딩 도달을 기록한다. 처음 도달한 엔딩이면 true.</summary>
        public bool RecordEnding(EndingResult result)
        {
            string key = EndingKey(result);
            int n;
            bool first = !_endings.TryGetValue(key, out n);
            if (first && _endings.Count >= MaxEntries) return false;
            _endings[key] = n + 1;
            return first;
        }

        /// <summary>본 CG 를 기록한다(대소문자 구분 없이 대문자로 저장). 처음 본 CG 면 true.</summary>
        public bool RecordCg(string cgId)
        {
            string id = NormalizeId(cgId);
            if (id == null || _cgs.Contains(id) || _cgs.Count >= MaxEntries) return false;
            _cgs.Add(id);
            return true;
        }

        public bool IsUnlocked(string unlockId)
        {
            return unlockId != null && _unlocks.Contains(unlockId);
        }

        /// <summary>해금 id 를 추가한다(UnlockService 가 사용). 새로 추가했으면 true.</summary>
        public bool AddUnlock(string unlockId)
        {
            if (string.IsNullOrEmpty(unlockId) || unlockId.Length > MaxKeyLength || _unlocks.Contains(unlockId) || _unlocks.Count >= MaxEntries) return false;
            _unlocks.Add(unlockId);
            return true;
        }

        private static string NormalizeId(string id)
        {
            string t = (id ?? string.Empty).Trim();
            if (t.Length == 0 || t.Length > MaxKeyLength) return null;
            return t.ToUpperInvariant();
        }

        // ---------- 저장/불러오기 ----------

        /// <summary>저장소에서 읽는다. 없으면 Missing, 깨졌으면 Corrupt(둘 다 빈 프로필). 예외를 던지지 않는다.</summary>
        public ProfileLoadStatus Load()
        {
            _endings.Clear();
            _cgs.Clear();
            _unlocks.Clear();
            string text = _storage == null ? null : _storage.Read();
            if (string.IsNullOrEmpty(text))
            {
                LastLoadStatus = ProfileLoadStatus.Missing;
                return LastLoadStatus;
            }
            try
            {
                Dictionary<string, object> root = MiniJson.Parse(text) as Dictionary<string, object>;
                if (root == null) throw new FormatException("root is not an object");
                ReadEndings(root);
                ReadList(root, "cgs", _cgs, true);
                ReadList(root, "unlocks", _unlocks, false);
                LastLoadStatus = ProfileLoadStatus.Loaded;
            }
            catch (FormatException)
            {
                _endings.Clear();
                _cgs.Clear();
                _unlocks.Clear();
                LastLoadStatus = ProfileLoadStatus.Corrupt;
                ICorruptPreserver keep = _storage as ICorruptPreserver;
                if (keep != null) keep.PreserveCorrupt(); // 다음 Save() 가 원본을 덮어쓰기 전에 .corrupt 로 보존
            }
            return LastLoadStatus;
        }

        private void ReadEndings(Dictionary<string, object> root)
        {
            object o;
            Dictionary<string, object> d;
            if (!root.TryGetValue("endings", out o) || (d = o as Dictionary<string, object>) == null) return;
            foreach (KeyValuePair<string, object> p in d)
            {
                if (p.Key.Length == 0 || p.Key.Length > MaxKeyLength || _endings.Count >= MaxEntries) continue;
                if (!(p.Value is double)) continue;
                double n = (double)p.Value;
                if (double.IsNaN(n) || n < 1.0) continue;
                _endings[p.Key] = n > 9999.0 ? 9999 : (int)n;
            }
        }

        private static void ReadList(Dictionary<string, object> root, string key, List<string> target, bool upper)
        {
            object o;
            List<object> list;
            if (!root.TryGetValue(key, out o) || (list = o as List<object>) == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                string s = list[i] as string;
                if (s == null) continue;
                s = s.Trim();
                if (s.Length == 0 || s.Length > MaxKeyLength || target.Count >= MaxEntries) continue;
                if (upper) s = s.ToUpperInvariant();
                if (!target.Contains(s)) target.Add(s);
            }
        }

        /// <summary>저장소에 쓴다. 저장소가 없거나 실패하면 false(게임은 계속된다).</summary>
        public bool Save()
        {
            return _storage != null && _storage.Write(ToJson());
        }

        public string ToJson()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"version\": ").Append(CurrentVersion).Append(",\n");
            sb.Append("  \"endings\": {");
            List<string> keys = new List<string>(_endings.Keys);
            keys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(MiniJson.Quote(keys[i])).Append(": ").Append(_endings[keys[i]]);
            }
            sb.Append("},\n");
            AppendList(sb, "cgs", _cgs, ",\n");
            AppendList(sb, "unlocks", _unlocks, "\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        private static void AppendList(StringBuilder sb, string key, List<string> list, string tail)
        {
            sb.Append("  \"").Append(key).Append("\": [");
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(MiniJson.Quote(list[i]));
            }
            sb.Append("]").Append(tail);
        }
    }

    /// <summary>
    /// 범용 해금 서비스(순수): 규칙(해금 id + 프로필 조건)을 등록하고, 평가해서 새로 해금된 id 를 프로필에 기록한다.
    /// 선택 콘텐츠 팩이 붙을 훅일 뿐 내용은 없다. 기본 규칙: 엔딩을 하나라도 보면 <see cref="ExtraEpilogues"/> 해금.
    /// </summary>
    public sealed class UnlockService
    {
        public const string ExtraEpilogues = "extra_epilogues";

        private readonly ProfileService _profile;
        private readonly List<string> _ids = new List<string>();
        private readonly List<Func<ProfileService, bool>> _conditions = new List<Func<ProfileService, bool>>();

        public UnlockService(ProfileService profile)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            _profile = profile;
        }

        /// <summary>기본 규칙을 등록한 서비스.</summary>
        public static UnlockService WithDefaults(ProfileService profile)
        {
            UnlockService s = new UnlockService(profile);
            s.Define(ExtraEpilogues, delegate (ProfileService p) { return p.ReachedCount > 0; });
            return s;
        }

        /// <summary>규칙 등록(같은 id 면 조건 교체).</summary>
        public void Define(string unlockId, Func<ProfileService, bool> condition)
        {
            if (string.IsNullOrEmpty(unlockId)) throw new ArgumentException("unlockId");
            if (condition == null) throw new ArgumentNullException("condition");
            int i = _ids.IndexOf(unlockId);
            if (i >= 0) { _conditions[i] = condition; return; }
            _ids.Add(unlockId);
            _conditions.Add(condition);
        }

        public bool IsUnlocked(string unlockId)
        {
            return _profile.IsUnlocked(unlockId);
        }

        /// <summary>모든 규칙을 평가해 새로 해금된 id 들을 프로필에 기록하고 돌려준다(저장은 호출자가 ProfileService.Save). 한 번 해금된 것은 되돌리지 않는다.</summary>
        public List<string> Evaluate()
        {
            List<string> added = new List<string>();
            for (int i = 0; i < _ids.Count; i++)
            {
                if (_profile.IsUnlocked(_ids[i])) continue;
                bool ok;
                try { ok = _conditions[i](_profile); }
                catch (Exception) { ok = false; }
                if (ok && _profile.AddUnlock(_ids[i])) added.Add(_ids[i]);
            }
            return added;
        }
    }

    /// <summary>엔딩 갤러리 칸 한 개.</summary>
    public sealed class EndingSlot
    {
        public string Key = string.Empty;
        public string Title = string.Empty;
        public bool Reached;
        public int Count;
    }

    /// <summary>도달 가능한 엔딩 칸 목록(순수): 인물별 노멀/굿/트루 18 + 공동 2인/3인 + 커뮤니티 = 21칸.</summary>
    public static class EndingGallery
    {
        public static List<EndingSlot> Slots(ProfileService profile, Func<string, string> nameOf)
        {
            List<EndingSlot> list = new List<EndingSlot>();
            string[] kinds = { "normal", "good", "true" };
            string[] kindNames = { Loc.T("ending_kind_normal", "노멀"), Loc.T("ending_kind_good", "굿"), Loc.T("ending_kind_true", "트루") };
            for (int c = 0; c < CharIds.All.Length; c++)
            {
                string id = CharIds.All[c];
                string name = nameOf != null ? nameOf(id) : id;
                for (int k = 0; k < kinds.Length; k++)
                    list.Add(Make(profile, kinds[k] + ":" + id, Loc.F("gallery_slot_title", "{0} {1} 엔딩", name, kindNames[k])));
            }
            list.Add(Make(profile, EndingTable.HaremDuo, EndingPlan.TitleOf(EndingKind.HaremDuo)));
            list.Add(Make(profile, EndingTable.HaremTrio, EndingPlan.TitleOf(EndingKind.HaremTrio)));
            list.Add(Make(profile, EndingTable.Community, EndingPlan.TitleOf(EndingKind.Community)));
            return list;
        }

        public static int TotalSlots
        {
            get { return CharIds.All.Length * 3 + 3; }
        }

        private static EndingSlot Make(ProfileService profile, string key, string title)
        {
            EndingSlot s = new EndingSlot();
            s.Key = key;
            s.Title = title;
            s.Count = profile == null ? 0 : profile.ReachCount(key);
            s.Reached = s.Count > 0;
            return s;
        }
    }

    /// <summary>선택지 표시 규칙(순수): 조건 미충족 선택지를 회색 + 힌트로 보일지 숨길지. 숨은 열쇠의 구체 조건은 드러내지 않는다.</summary>
    public static class ChoiceLabel
    {
        public static string LockedHint { get { return Loc.T("choice_locked_hint", " (조건 미충족)"); } }

        /// <summary>표시할 문구. 숨겨야 하면 null.</summary>
        public static string Format(string text, bool available, bool showLocked)
        {
            if (available) return text;
            return showLocked ? text + LockedHint : null;
        }
    }
}
