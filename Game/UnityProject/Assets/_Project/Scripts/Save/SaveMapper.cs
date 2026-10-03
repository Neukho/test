using System;
using System.Collections.Generic;
using MorpheHouse.Core;
using MorpheHouse.Data;

namespace MorpheHouse.Save
{
    /// <summary>GameState ↔ SaveData 변환. 저장은 아침 단계에서만 허용한다.</summary>
    public static class SaveMapper
    {
        public static SaveData ToSave(GameState s, int slot, string appVersion)
        {
            if (s.Phase != DayPhase.Morning)
                throw new InvalidOperationException("Saving is only allowed in the Morning phase.");

            SaveData d = new SaveData();
            d.Slot = slot;
            d.SavedAtUtc = DateTime.UtcNow.ToString("o");
            d.AppVersion = appVersion;
            d.PlayerName = s.PlayerName;
            d.Gender = s.Gender;
            d.PlayerSpecies = s.PlayerSpecies;
            d.PlayerAppearance = s.PlayerAppearance;
            d.Difficulty = (int)s.Difficulty;
            d.Chapter = s.Chapter;
            d.Day = s.Day;
            d.DayInChapter = s.DayInChapter;
            d.Gold = s.Gold;
            d.Reputation = s.Reputation;
            d.Fatigue = s.Fatigue;
            d.ShopLevel = s.ShopLevel;
            d.SatisfactionBonus = s.SatisfactionBonus;
            d.LockedRoute = s.LockedRoute;
            for (int i = 0; i < s.LockedGroup.Count; i++) d.LockedGroup.Add(s.LockedGroup[i]);
            d.PlayTimeSec = s.PlayTimeSec;

            foreach (KeyValuePair<string, CharState> p in s.Chars)
            {
                CharSave c = new CharSave();
                c.Id = p.Key;
                c.Aff = p.Value.Aff;
                c.Trust = p.Value.Trust;
                c.EventsDone = p.Value.EventsDone;
                c.LastGiftDay = p.Value.LastGiftDay;
                c.LastDateDay = p.Value.LastDateDay;
                c.LastContactDay = p.Value.LastContactDay;
                d.Chars.Add(c);
            }
            foreach (KeyValuePair<string, int> p in s.Flags)
            {
                FlagSave f = new FlagSave();
                f.Key = p.Key;
                f.Value = p.Value;
                d.Flags.Add(f);
            }
            for (int i = 0; i < s.VisitedNodes.Count; i++) d.VisitedNodes.Add(s.VisitedNodes[i]);
            return d;
        }

        public static GameState FromSave(SaveData d)
        {
            GameState s = new GameState();
            s.PlayerName = d.PlayerName;
            s.Gender = d.Gender;
            s.PlayerSpecies = string.IsNullOrEmpty(d.PlayerSpecies) ? "human" : d.PlayerSpecies;
            s.PlayerAppearance = d.PlayerAppearance < 1 ? 1 : d.PlayerAppearance;
            s.Difficulty = (Difficulty)d.Difficulty;
            s.Chapter = d.Chapter;
            s.Day = d.Day;
            s.DayInChapter = d.DayInChapter;
            s.Phase = DayPhase.Morning;
            s.Gold = d.Gold;
            s.Reputation = d.Reputation;
            s.Fatigue = d.Fatigue;
            s.ShopLevel = d.ShopLevel;
            s.SatisfactionBonus = d.SatisfactionBonus;
            // 구성원 목록이 있으면 그것이 기준(대표 id 는 첫 구성원). 없으면 구형 저장: LockedRoute 만 유지(LockedIds() 가 [LockedRoute] 로 해석).
            s.LockedRoute = d.LockedRoute ?? string.Empty;
            if (d.LockedGroup != null && d.LockedGroup.Count > 0)
            {
                s.SetLock(d.LockedGroup);
            }
            s.PlayTimeSec = d.PlayTimeSec;

            for (int i = 0; i < CharIds.All.Length; i++) s.Chars[CharIds.All[i]] = new CharState();
            for (int i = 0; i < d.Chars.Count; i++)
            {
                CharSave c = d.Chars[i];
                CharState st = new CharState();
                st.Aff = c.Aff;
                st.Trust = c.Trust;
                st.EventsDone = c.EventsDone;
                st.LastGiftDay = c.LastGiftDay;
                st.LastDateDay = c.LastDateDay;
                st.LastContactDay = c.LastContactDay;
                s.Chars[c.Id] = st;
            }
            for (int i = 0; i < d.Flags.Count; i++) s.Flags[d.Flags[i].Key] = d.Flags[i].Value;
            if (d.VisitedNodes != null)
            {
                for (int i = 0; i < d.VisitedNodes.Count; i++)
                    if (!string.IsNullOrEmpty(d.VisitedNodes[i])) s.VisitedNodes.Add(d.VisitedNodes[i]);
            }
            return s;
        }
    }
}
