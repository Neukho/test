using System;
using System.Collections.Generic;
using MorpheHouse.Data;

namespace MorpheHouse.Tycoon
{
    /// <summary>
    /// 사람 없이 8챕터를 끝까지 돌려 보는 검증용 플레이어(경영만, 연애 제외).
    /// 정책: 여유 자금(15%)이 있으면 가게 확장, 피로 70 이상이면 휴식.
    /// </summary>
    public static class AutoPlayer
    {
        public sealed class Summary
        {
            public int Runs;
            public int Days;
            public float FinalGold;
            public float FinalReputation;
            public float FinalLevel;
            public float[] ChapterAverageGross;
        }

        public static Summary Run(BalanceTable balance, int runs, int seed)
        {
            Summary sum = new Summary();
            sum.Runs = runs;
            sum.ChapterAverageGross = new float[balance.MaxChapter];

            for (int run = 0; run < runs; run++)
            {
                GameState state = GameState.NewGame(balance, balance.Difficulty, "auto");
                DayCycle cycle = new DayCycle(state, balance, new SystemRandomAdapter(seed + run));
                float[] grossSum = new float[balance.MaxChapter];
                int[] dayCount = new int[balance.MaxChapter];

                cycle.Begin();
                while (!state.Finished)
                {
                    float cost;
                    while (cycle.CanUpgradeShop(out cost) && state.Gold >= cost * 1.15f) cycle.TryUpgradeShop();

                    int chapterIndex = state.Chapter - 1;
                    DayResult r = cycle.RunBusiness();
                    grossSum[chapterIndex] += r.Gross;
                    dayCount[chapterIndex]++;

                    cycle.BeginNight();
                    while (state.ActionsLeft > 0)
                    {
                        if (state.Fatigue >= 70f) cycle.DoNightAction(Core.NightAction.Rest);
                        else break;
                    }
                    cycle.Settle();
                    cycle.NextDay();
                }

                sum.Days += state.Day - 1;
                sum.FinalGold += state.Gold;
                sum.FinalReputation += state.Reputation;
                sum.FinalLevel += state.ShopLevel;
                for (int c = 0; c < balance.MaxChapter; c++)
                    sum.ChapterAverageGross[c] += dayCount[c] > 0 ? grossSum[c] / dayCount[c] : 0f;
            }

            sum.Days /= Math.Max(1, runs);
            sum.FinalGold /= runs;
            sum.FinalReputation /= runs;
            sum.FinalLevel /= runs;
            for (int c = 0; c < balance.MaxChapter; c++) sum.ChapterAverageGross[c] /= runs;
            return sum;
        }
    }
}
