using System;
using MorpheHouse.Data;

namespace MorpheHouse.Romance
{
    /// <summary>연애 규칙의 순수 공식 모음(상태·난수·이벤트 버스 없음). GDD §4.4 와 simulate_balance.py 가 기준.</summary>
    public static class RomanceRules
    {
        /// <summary>루트 확정이 가능한 최소 챕터.</summary>
        public const int LockMinChapter = 7;
        /// <summary>루트 확정에 필요한 최소 호감도.</summary>
        public const float LockMinAff = 60f;
        /// <summary>루트 확정에 필요한 최소 신뢰도.</summary>
        public const float LockMinTrust = 20f;
        /// <summary>공동(다자) 루트의 최대 인원.</summary>
        public const int MaxGroupSize = 3;
        /// <summary>좋아하는 데이트 스팟의 호감도 보너스 배율.</summary>
        public const float LikedSpotMultiplier = 1.25f;
        /// <summary>싫어하는 데이트 스팟의 호감도 배율.</summary>
        public const float DislikedSpotMultiplier = 0.75f;

        /// <summary>챕터별 호감도 상한(chapters.csv aff_cap).</summary>
        public static float AffCap(BalanceTable balance, int chapter)
        {
            return balance.Chapter(chapter).AffCap;
        }

        /// <summary>챕터별 신뢰도 상한(chapters.csv trust_cap).</summary>
        public static float TrustCap(BalanceTable balance, int chapter)
        {
            return balance.Chapter(chapter).TrustCap;
        }

        /// <summary>호감도 증가량 감쇠: 양수 증가분에만 gain * (1 - soft * aff/100). 음수는 그대로.</summary>
        public static float DiminishedGain(float gain, float currentAff, float softDiminish)
        {
            if (gain <= 0f) return gain;
            return gain * (1f - softDiminish * (currentAff / 100f));
        }

        /// <summary>0..cap 으로 자른다.</summary>
        public static float Clamp(float value, float cap)
        {
            return Math.Max(0f, Math.Min(cap, value));
        }

        /// <summary>
        /// 방치 감쇠 미리보기(UI 경고용): 접촉 없이 grace 일을 초과했으면 하루 감소량, 아니면 0.
        /// 실제 적용은 DayCycle 이 아침마다 한다. 이 서비스는 감쇠를 적용하지 않는다.
        /// </summary>
        public static float DailyDecay(int daysSinceContact, float graceDays, float decayPerDay)
        {
            return daysSinceContact > graceDays ? decayPerDay : 0f;
        }

        /// <summary>감쇠가 시작되기까지 남은 일수(이미 시작되었으면 0).</summary>
        public static int DaysUntilDecay(int daysSinceContact, float graceDays)
        {
            int left = (int)Math.Floor(graceDays) - daysSinceContact + 1;
            return left < 0 ? 0 : left;
        }

        /// <summary>데이트 스팟 취향에 따른 호감도 배율.</summary>
        public static float SpotMultiplier(Preference pref)
        {
            if (pref == Preference.Like) return LikedSpotMultiplier;
            if (pref == Preference.Dislike) return DislikedSpotMultiplier;
            return 1f;
        }

        /// <summary>선물 취향 → actions.csv 행 이름.</summary>
        public static string GiftActionKey(Preference pref)
        {
            if (pref == Preference.Like) return "gift_like";
            if (pref == Preference.Dislike) return "gift_dislike";
            return "gift_neutral";
        }

        /// <summary>루트 확정 가능 여부(챕터 7 이상, 호감 60 이상, 신뢰 20 이상).</summary>
        public static bool CanLockRoute(int chapter, float aff, float trust)
        {
            return chapter >= LockMinChapter && aff >= LockMinAff && trust >= LockMinTrust;
        }

        /// <summary>이벤트 해금 조건(events.csv): 챕터·호감·신뢰 임계값을 모두 만족해야 한다.</summary>
        public static bool EventUnlocked(EventRow row, int chapter, float aff, float trust)
        {
            return chapter >= row.ChapterMin && aff >= row.AffMin && trust >= row.TrustMin;
        }
    }
}
