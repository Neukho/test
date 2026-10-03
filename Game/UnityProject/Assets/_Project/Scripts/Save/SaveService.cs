using System;
using System.IO;

namespace MorpheHouse.Save
{
    public enum LoadStatus
    {
        Ok,
        NotFound,
        RestoredFromBackup,
        Tampered,
        Corrupt
    }

    public sealed class LoadResult
    {
        public LoadStatus Status;
        public SaveData Data;
        public string Detail = string.Empty;
        /// <summary>RestoredFromBackup 일 때, 주 파일이 왜 쓰이지 못했는지(Tampered/Corrupt/NotFound). 그 외에는 Ok 와 같은 의미 없는 값(NotFound).</summary>
        public LoadStatus PrimaryStatus = LoadStatus.NotFound;
        /// <summary>true 면 주 파일이 변조/손상되어 직전 저장(.bak)을 대신 읽었다(진행이 한 번 되돌아갔을 수 있음). UI 는 사용자에게 알려야 한다.</summary>
        public bool PrimaryRejected;
        /// <summary>파일에 기록돼 있던 스키마 버전(마이그레이션 전). 파싱 전에는 0.</summary>
        public int FileVersion;
    }

    /// <summary>JSON 직렬화 추상화(Unity: JsonUtility, 테스트: 다른 구현).</summary>
    public interface ISerializer
    {
        string ToJson(SaveData data);
        SaveData FromJson(string json);
    }

    /// <summary>저장소 추상화. 원자적 쓰기와 백업은 구현체 책임.</summary>
    public interface ISaveStorage
    {
        void Write(string name, string text);
        string Read(string name);
        string ReadBackup(string name);
        bool Exists(string name);
    }

    /// <summary>손상된 현재 파일을 보존용으로 치워 두는 저장소(선택 구현). SaveService.Save 가 덮어쓰기 전에 호출한다.</summary>
    public interface IQuarantinableStorage
    {
        void Quarantine(string name);
    }

    /// <summary>임시 파일에 쓴 뒤 교체하는 원자적 파일 저장소. 교체 전 파일은 .bak 으로 보존한다.</summary>
    public sealed class FileSaveStorage : ISaveStorage, IQuarantinableStorage
    {
        private readonly string _directory;

        public FileSaveStorage(string directory)
        {
            _directory = directory;
            Directory.CreateDirectory(directory);
        }

        /// <summary>저장 파일 최대 크기(바이트). 넘으면 손상으로 취급한다(읽기 전 크기 검사).</summary>
        public const long MaxFileBytes = 1024 * 1024;

        private string PathOf(string name)
        {
            // 경로 조작 방지: 이름은 순수한 파일명이어야 한다(빈 이름·구분자·"."·".." 거부).
            string file = name == null ? string.Empty : Path.GetFileName(name);
            if (file.Length == 0 || file == "." || file == ".." || file != name)
                throw new ArgumentException("invalid save file name", "name");
            return Path.Combine(_directory, file);
        }

        private static string ReadCapped(string path)
        {
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > MaxFileBytes) throw new IOException("save file too large");
            return File.ReadAllText(path);
        }

        public void Write(string name, string text)
        {
            string path = PathOf(name);
            string tmp = path + ".tmp";
            string bak = path + ".bak";
            File.WriteAllText(tmp, text);
            if (File.Exists(path))
            {
                // 가능하면 원자적 교체(직전 파일은 .bak 으로). 지원하지 않는 환경에서만 수동 순서로 폴백.
                try
                {
                    File.Replace(tmp, path, bak);
                    return;
                }
                catch (Exception ex)
                {
                    if (!(ex is IOException || ex is PlatformNotSupportedException || ex is UnauthorizedAccessException)) throw;
                    if (!File.Exists(tmp)) throw;
                }
                File.Copy(path, bak, true);
                File.Delete(path);
            }
            File.Move(tmp, path);
        }

        /// <summary>검증되지 않은(손상·변조) 현재 파일을 &lt;name&gt;.corrupt 로 옮겨 보존한다. 이후 Write 가 정상 .bak 을 덮어쓰지 않는다.</summary>
        public void Quarantine(string name)
        {
            string path = PathOf(name);
            if (!File.Exists(path)) return;
            string dest = path + ".corrupt";
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(path, dest);
        }

        public string Read(string name)
        {
            string path = PathOf(name);
            return ReadCapped(path);
        }

        public string ReadBackup(string name)
        {
            string path = PathOf(name) + ".bak";
            return ReadCapped(path);
        }

        public bool Exists(string name)
        {
            return File.Exists(PathOf(name)) || File.Exists(PathOf(name) + ".bak");
        }
    }

    /// <summary>슬롯 저장/불러오기. 슬롯 0 = 자동 저장, 1~3 = 수동 슬롯.</summary>
    public sealed class SaveService
    {
        public const int AutoSlot = 0;
        public const int MaxSlot = 3;

        private readonly ISerializer _serializer;
        private readonly ISaveStorage _storage;

        public SaveService(ISerializer serializer, ISaveStorage storage)
        {
            _serializer = serializer;
            _storage = storage;
        }

        public static string SlotName(int slot)
        {
            if (slot < 0 || slot > MaxSlot) throw new ArgumentOutOfRangeException("slot");
            return "slot" + slot + ".json";
        }

        public bool Exists(int slot)
        {
            return _storage.Exists(SlotName(slot));
        }

        public void Save(int slot, SaveData data)
        {
            string problem = SaveValidator.Validate(data);
            if (problem != null) throw new InvalidOperationException("refusing to save invalid data: " + problem);

            // 현재 파일이 정상이 아니면 .bak(직전 정상본)을 덮어쓰지 않도록 먼저 .corrupt 로 보존한다.
            IQuarantinableStorage quarantine = _storage as IQuarantinableStorage;
            if (quarantine != null)
            {
                string slotName = SlotName(slot);
                LoadResult current = TryRead(delegate () { return _storage.Read(slotName); });
                if (current.Status != LoadStatus.Ok && current.Status != LoadStatus.NotFound)
                {
                    try { quarantine.Quarantine(slotName); } catch (Exception) { /* 보존 실패는 저장을 막지 않음 */ }
                }
            }

            data.Slot = slot;
            data.Version = SaveData.CurrentVersion;
            // 정규 페이로드는 직렬화기와 무관하지만, 직렬화기가 값을 반올림하면 로드된 값이 달라질 수 있으므로
            // "실제로 저장되어 다시 읽힐 값"에 서명한다(직렬화 -> 역직렬화 -> 서명 -> 해시만 교체).
            data.Hash = string.Empty;
            SaveData reread = _serializer.FromJson(_serializer.ToJson(data));
            if (reread == null) throw new InvalidOperationException("serializer round-trip failed");
            data.Hash = SaveIntegrity.Sign(reread);
            string signed = _serializer.ToJson(data);
            _storage.Write(SlotName(slot), signed);
        }

        public LoadResult Load(int slot)
        {
            string name = SlotName(slot);
            LoadResult primary = TryRead(delegate () { return _storage.Read(name); });
            if (primary.Status == LoadStatus.Ok) return primary;

            LoadResult backup = TryRead(delegate () { return _storage.ReadBackup(name); });
            if (backup.Status == LoadStatus.Ok)
            {
                backup.Status = LoadStatus.RestoredFromBackup;
                backup.Detail = "primary: " + primary.Status;
                backup.PrimaryStatus = primary.Status;
                backup.PrimaryRejected = primary.Status == LoadStatus.Tampered || primary.Status == LoadStatus.Corrupt;
                return backup;
            }
            return primary; // 사유(NotFound/Tampered/Corrupt)를 그대로 반환
        }

        /// <summary>
        /// v1 저장(서명 = 이 필드를 비운 재직렬화 텍스트의 HMAC)의 호환 검증. 이 저장소 이력의 v1 변형 셋을 모두 받는다:
        /// (a) 3단계 이후(전체 현재 스키마), (b) 2단계(LockedGroup 없음), (c) 1단계(PlayerSpecies/PlayerAppearance/VisitedNodes/LockedGroup 없음).
        /// 뺀 필드는 기본값(비어 있음/human/1)일 때만 해당 변형을 시도하므로, 값이 있는 필드는 항상 서명 범위 안이다.
        /// v2 이후에는 쓰지 않는다(정규 페이로드 사용).
        /// </summary>
        private bool VerifyLegacyV1(SaveData data, string stored)
        {
            data.Hash = string.Empty;
            string rebuilt = _serializer.ToJson(data);
            data.Hash = stored;
            if (SaveIntegrity.ConstantTimeEquals(stored, SaveIntegrity.Compute(rebuilt))) return true;
            bool noGroup = data.LockedGroup == null || data.LockedGroup.Count == 0;
            if (!noGroup) return false;
            string stage2 = StripEmptyLockedGroup(rebuilt);
            if (SaveIntegrity.ConstantTimeEquals(stored, SaveIntegrity.Compute(stage2))) return true;
            bool noStage2 = data.PlayerSpecies == "human" && data.PlayerAppearance == 1 && (data.VisitedNodes == null || data.VisitedNodes.Count == 0);
            if (!noStage2) return false;
            string stage1 = StripLegacyField(StripLegacyField(StripLegacyField(stage2, "PlayerSpecies"), "PlayerAppearance"), "VisitedNodes");
            return SaveIntegrity.ConstantTimeEquals(stored, SaveIntegrity.Compute(stage1));
        }

        /// <summary>직렬화 문자열에서 비어 있는 LockedGroup 항목(JSON 배열 또는 XML 빈 요소)을 제거한다. 구형 저장 해시 호환용.</summary>
        public static string StripEmptyLockedGroup(string text)
        {
            return StripLegacyField(text, "LockedGroup");
        }

        /// <summary>직렬화 문자열에서 한 필드 항목(JSON "Name":값, XML 요소)을 제거한다(2·3단계 이전 v1 해시 재구성용). 고정 패턴.</summary>
        public static string StripLegacyField(string text, string name)
        {
            if (text == null) return null;
            string n = System.Text.RegularExpressions.Regex.Escape(name);
            string json = System.Text.RegularExpressions.Regex.Replace(text, "\\s*\"" + n + "\"\\s*:\\s*(\\[\\s*\\]|\"[^\"\\\\]*\"|-?[0-9.eE+-]+)\\s*,?", string.Empty);
            json = System.Text.RegularExpressions.Regex.Replace(json, "\\s*<" + n + "\\s*/>", string.Empty);
            return System.Text.RegularExpressions.Regex.Replace(json, "\\s*<" + n + ">[^<]*</" + n + ">", string.Empty);
        }

        /// <summary>저장소 읽기 예외(크기 초과·I/O 오류 등)는 Corrupt 로 변환한다. Load 는 예외를 던지지 않는다.</summary>
        private LoadResult TryRead(Func<string> read)
        {
            string text;
            try
            {
                text = read();
            }
            catch (Exception ex)
            {
                LoadResult r = new LoadResult();
                r.Status = LoadStatus.Corrupt;
                r.Detail = "storage: " + ex.GetType().Name;
                return r;
            }
            return TryParse(text);
        }

        private LoadResult TryParse(string text)
        {
            LoadResult r = new LoadResult();
            if (text == null)
            {
                r.Status = LoadStatus.NotFound;
                return r;
            }

            SaveData data;
            try
            {
                data = _serializer.FromJson(text);
            }
            catch (Exception ex)
            {
                r.Status = LoadStatus.Corrupt;
                r.Detail = ex.GetType().Name;
                return r;
            }
            if (data == null)
            {
                r.Status = LoadStatus.Corrupt;
                return r;
            }

            r.FileVersion = data.Version;
            if (data.Version < 1 || data.Version > SaveData.CurrentVersion)
            {
                r.Status = LoadStatus.Corrupt;
                r.Detail = "unsupported version " + data.Version;
                return r;
            }

            string stored = data.Hash;
            bool signatureOk;
            if (data.Version >= 2)
            {
                signatureOk = SaveIntegrity.ConstantTimeEquals(stored, SaveIntegrity.Sign(data));
            }
            else
            {
                signatureOk = VerifyLegacyV1(data, stored);
            }
            if (!signatureOk)
            {
                r.Status = LoadStatus.Tampered;
                return r;
            }
            data.Hash = stored;
            // 서명 검증 후에만 마이그레이션(변조된 입력을 마이그레이션하지 않음).
            if (!SaveMigrations.Migrate(data))
            {
                r.Status = LoadStatus.Corrupt;
                r.Detail = "migration failed from " + r.FileVersion;
                return r;
            }

            string problem = SaveValidator.Validate(data);
            if (problem != null)
            {
                r.Status = LoadStatus.Corrupt;
                r.Detail = problem;
                return r;
            }

            r.Status = LoadStatus.Ok;
            r.Data = data;
            return r;
        }
    }
}
