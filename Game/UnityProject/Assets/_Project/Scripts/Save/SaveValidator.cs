using System;

namespace MorpheHouse.Save
{
    /// <summary>
    /// 로드한 데이터의 범위·형식 검증. 변조·손상된 값이 게임 상태로 들어가는 것을 막는다.
    /// 알 수 없는 필드는 역직렬화 단계에서 무시된다.
    /// </summary>
    public static class SaveValidator
    {
        /// <summary>유효하면 null, 아니면 사유 문자열.</summary>
        public static string Validate(SaveData d)
        {
            if (d == null) return "null";
            if (d.Version < 1 || d.Version > SaveData.CurrentVersion) return "unsupported version " + d.Version;
            if (d.Difficulty < 0 || d.Difficulty > 2) return "difficulty";
            if (d.Chapter < 1 || d.Chapter > 8) return "chapter";
            if (d.Day < 1 || d.Day > 1000) return "day";
            if (d.DayInChapter < 1 || d.DayInChapter > 60) return "dayInChapter";
            if (!IsFinite(d.Gold) || d.Gold < -1000000f || d.Gold > 1000000000f) return "gold";
            if (!IsFinite(d.Reputation) || d.Reputation < 0f || d.Reputation > 100f) return "reputation";
            if (!IsFinite(d.Fatigue) || d.Fatigue < 0f || d.Fatigue > 100f) return "fatigue";
            if (d.ShopLevel < 1 || d.ShopLevel > 10) return "shopLevel";
            if (d.Slot < 0 || d.Slot > 3) return "slot";
            if (!IsFinite(d.SatisfactionBonus) || d.SatisfactionBonus < -1f || d.SatisfactionBonus > 1f) return "satisfactionBonus";
            if (double.IsNaN(d.PlayTimeSec) || double.IsInfinity(d.PlayTimeSec) || d.PlayTimeSec < 0.0 || d.PlayTimeSec > 3.2e8) return "playTime";
            if (d.SavedAtUtc != null && d.SavedAtUtc.Length > 64) return "savedAt";
            if (d.AppVersion != null && d.AppVersion.Length > 64) return "appVersion";
            if (d.Hash != null && d.Hash.Length > 128) return "hash";
            if (d.PlayerName == null || d.PlayerName.Length == 0 || d.PlayerName.Length > MorpheHouse.Data.ProfileValidator.MaxNameUtf16) return "playerName";
            // 새 게임 생성 화면과 같은 규칙(<>{}\ · 제어/방향/제로폭 문자 · 짝 없는 서로게이트 · 텍스트 요소 수) — 공유된 세이브의 이름 주입 방지
            if (MorpheHouse.Data.ProfileValidator.CheckName(d.PlayerName).Length != 0) return "playerName chars";
            if (d.Chars == null || d.Flags == null) return "lists";
            if (d.Chars.Count > 64 || d.Flags.Count > 1024) return "list size";
            if (!MorpheHouse.Data.ProfileValidator.IsValidGender(d.Gender ?? string.Empty)) return "gender";
            if (!MorpheHouse.Data.ProfileValidator.IsValidSpecies(d.PlayerSpecies ?? string.Empty)) return "species";
            if (!MorpheHouse.Data.ProfileValidator.IsValidAppearance(d.PlayerAppearance)) return "appearance";
            if (d.LockedRoute != null && d.LockedRoute.Length > 32) return "lockedRoute";
            if (!string.IsNullOrEmpty(d.LockedRoute) && !IsKnownChar(d.LockedRoute)) return "lockedRoute unknown";
            if (d.LockedGroup != null && d.LockedGroup.Count > 0)
            {
                if (d.LockedGroup.Count > 3) return "lockedGroup size";
                for (int i = 0; i < d.LockedGroup.Count; i++)
                {
                    string m = d.LockedGroup[i];
                    if (string.IsNullOrEmpty(m) || m.Length > 32) return "lockedGroup member";
                    if (!IsKnownChar(m)) return "lockedGroup unknown";
                    for (int j = 0; j < i; j++)
                        if (d.LockedGroup[j] == m) return "lockedGroup duplicate";
                }
                if (d.LockedRoute != d.LockedGroup[0]) return "lockedGroup primary";
            }
            if (d.VisitedNodes != null)
            {
                if (d.VisitedNodes.Count > 4096) return "visited size";
                for (int i = 0; i < d.VisitedNodes.Count; i++)
                {
                    string v = d.VisitedNodes[i];
                    if (v == null || v.Length > 128) return "visited node";
                    if (!IsCleanText(v) || v.IndexOf('/') >= 0 || v.IndexOf('\\') >= 0 || v.Contains("..")) return "visited node chars";
                }
            }
            System.Collections.Generic.HashSet<string> flagKeys = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < d.Flags.Count; i++)
            {
                FlagSave f = d.Flags[i];
                if (f == null || string.IsNullOrEmpty(f.Key) || f.Key.Length > 64) return "flag key";
                if (!IsCleanText(f.Key)) return "flag key chars";
                if (!flagKeys.Add(f.Key)) return "flag key duplicate";
                if (f.Value < -1000000 || f.Value > 1000000) return "flag value";
            }

            System.Collections.Generic.HashSet<string> charIds = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < d.Chars.Count; i++)
            {
                CharSave c = d.Chars[i];
                if (c == null || string.IsNullOrEmpty(c.Id) || c.Id.Length > 32) return "char id";
                if (!IsKnownChar(c.Id)) return "char id unknown";
                if (!charIds.Add(c.Id)) return "char id duplicate";
                if (c.LastGiftDay < -100 || c.LastGiftDay > 5000) return "lastGiftDay";
                if (c.LastDateDay < -100 || c.LastDateDay > 5000) return "lastDateDay";
                if (c.LastContactDay < -100 || c.LastContactDay > 5000) return "lastContactDay";
                if (!IsFinite(c.Aff) || c.Aff < 0f || c.Aff > 100f) return "aff";
                if (!IsFinite(c.Trust) || c.Trust < 0f || c.Trust > 50f) return "trust";
                if (c.EventsDone < 0 || c.EventsDone > 5) return "eventsDone";
            }
            return null;
        }

        private static bool IsKnownChar(string id)
        {
            return System.Array.IndexOf(MorpheHouse.Data.CharIds.All, id) >= 0;
        }

        /// <summary>제어·방향·제로폭 문자와 짝 없는 서로게이트가 없으면 true.</summary>
        private static bool IsCleanText(string t)
        {
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (char.IsControl(c) || MorpheHouse.Data.ProfileValidator.IsInvisibleOrBidi(c)) return false;
                if (char.IsHighSurrogate(c)) { if (i + 1 >= t.Length || !char.IsLowSurrogate(t[i + 1])) return false; i++; }
                else if (char.IsLowSurrogate(c)) return false;
            }
            return true;
        }

        private static bool IsFinite(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }
    }
}
