using System;
using MorpheHouse.Data;

namespace MorpheHouse.Tycoon
{
    /// <summary>
    /// 하루 영업 매출 계산(순수 로직). tools/simulate_balance.py 와 같은 수식을 쓴다.
    /// 수식 변경 시 파이썬 시뮬레이터와 CSV 도 함께 갱신한다.
    /// </summary>
    public static class BusinessSimulator
    {
        private static readonly string[] FlavorEvents =
        {
            "단골 손님이 지인을 데려왔다.",
            "비가 와서 창가 자리가 한산했다.",
            "재료 도매가가 살짝 올랐다.",
            "진상 손님을 정중하게 달랬다."
        };

        public static DayResult Simulate(GameState state, BalanceTable b, IRandom rng)
        {
            ChapterRow chapter = b.Chapter(state.Chapter);
            DayResult r = new DayResult();

            float seats = b.P("seat_base") + b.P("seat_per_level") * state.ShopLevel;
            float turns = b.P("turns_base") + b.P("turns_per_level") * state.ShopLevel;

            float qualityBonus = 0f;
            float speedBonus = 0f;
            // 직원 = characters.csv 에서 staff_join_chapter > 0 인 인물(합류 챕터부터 영업 보너스). 데이터의 단일 출처.
            for (int i = 0; i < b.Characters.Count; i++)
            {
                int join = b.Characters[i].StaffJoinChapter;
                if (join <= 0 || state.Chapter < join) continue;
                StaffBand band = b.BandFor(state.AffinityOf(b.Characters[i].Id));
                qualityBonus += band.QualityBonus;
                speedBonus += band.SpeedBonus;
            }

            float capacity = seats * turns * (1f + speedBonus);
            float demand = b.P("demand_base") + b.P("demand_per_level") * state.ShopLevel + b.P("demand_per_rep") * state.Reputation;

            if (state.Chapter == 7)
            {
                // 체인점 진출: 평판이 60 이상이면 타격이 절반. 루트 확정 후에는 루트 인물의 도움으로 수용량 증가.
                float penalty = b.P("chain_demand_penalty") * (state.Reputation < 60f ? 1f : 0.5f);
                demand *= 1f - penalty;
                // 루트 확정 후 도움 인물 보너스(params.csv helper_bonus 의 설명대로): 확정 전에는 없다(QA-14).
                if (state.HasRouteLock) capacity *= 1f + b.P("helper_bonus");
            }

            float served = Math.Min(capacity, demand);
            float price = b.P("price_base") + b.P("price_per_menu") * chapter.MenuCount;

            double noise = rng.NextGaussian(0.0, b.P("sat_noise"));
            float satisfaction = Clamp((float)(b.P("sat_base") + b.P("prep_bonus") + state.SatisfactionBonus + qualityBonus + noise), 0.3f, 1f);

            float gross = served * price * (0.6f + 0.4f * satisfaction) * (1f / 0.96f) * b.P("rev_mult");
            float ingredient = gross * b.P("ingredient_cost_rate");
            float upkeep = b.P("upkeep_per_level") * state.ShopLevel;

            // 랜덤 이벤트: 대부분 연출 문구, 드물게 평판 ±10.
            float eventRep = 0f;
            if (rng.NextDouble() < b.P("random_event_prob"))
            {
                if (rng.NextDouble() < b.P("rep_event_prob") / b.P("random_event_prob"))
                {
                    bool bad = rng.NextDouble() < (1.0 / 3.0);
                    eventRep = (bad ? -1f : 1f) * b.P("rep_event_amp");
                    r.EventText = bad ? "나쁜 소문이 돌아 평판이 크게 떨어졌다." : "리뷰가 화제가 되어 평판이 크게 올랐다.";
                }
                else
                {
                    r.EventText = FlavorEvents[(int)(rng.NextDouble() * FlavorEvents.Length) % FlavorEvents.Length];
                }
            }

            r.Seats = seats;
            r.Capacity = capacity;
            r.Demand = demand;
            r.Served = served;
            r.Price = price;
            r.Satisfaction = satisfaction;
            r.Gross = gross;
            r.Ingredient = ingredient;
            r.Upkeep = upkeep;
            r.Net = gross - ingredient - upkeep;
            r.RepDelta = Clamp((satisfaction - b.P("rep_sat_center")) * 18f, -3f, 3f) + eventRep;
            r.FatigueAdded = Math.Max(8f, b.P("fatigue_business_base") - b.P("fatigue_per_staff") * chapter.StaffCount);
            return r;
        }

        private static float Clamp(float v, float min, float max)
        {
            return v < min ? min : (v > max ? max : v);
        }
    }
}
