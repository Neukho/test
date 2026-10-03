using System;
using System.Collections.Generic;
using MorpheHouse.Data;
using MorpheHouse.Romance;

namespace MorpheHouse.GameFlow
{
    /// <summary>
    /// 엔딩 재생 순서 계획(순수). EndingPlan(판정 + 대체 문구)을 받아 실제로 재생할 장면 목록으로 바꾼다.
    /// 단독 루트: &lt;Char&gt;_End_*(노드 안에서 다른 노드로 jump 해도 호스트는 한 노드로 취급). 공동: Ending_Harem2|3 → 구성원 순서대로 &lt;Char&gt;_Harem_Epilogue.
    /// 커뮤니티: Ending_Community. 노드가 없으면 대체 문구 장면.
    /// </summary>
    public static class EndingFlow
    {
        public static List<SceneItem> Scenes(EndingPlan plan, Func<string, bool> nodeExists)
        {
            if (plan == null) throw new ArgumentNullException("plan");
            List<SceneItem> list = new List<SceneItem>();
            Func<string, bool> exists = nodeExists ?? delegate (string n) { return true; };

            SceneItem main = new SceneItem();
            main.IntendedNode = plan.IntendedNode;
            main.Node = plan.NodeName;
            main.FallbackText = plan.FallbackText;
            list.Add(main);

            if (plan.Result.IsHarem)
            {
                for (int i = 0; i < plan.Result.Members.Count; i++)
                {
                    string id = plan.Result.Members[i];
                    string name = i < plan.MemberNames.Count ? plan.MemberNames[i] : id;
                    string node = RouteFlow.EpilogueNode(id);
                    list.Add(SceneItem.Of(node, exists(node), Loc.F("ending_fallback_epilogue", "({0}의 에필로그 시나리오는 준비 중입니다.) 함께하는 아침이 밝아 온다.", name)));
                }
            }
            return list;
        }
    }

    /// <summary>엔딩 화면에 보여 줄 요약(순수): 제목, 한 줄 부제, 핵심 통계 줄들.</summary>
    public sealed class EndSummary
    {
        public string Title = string.Empty;
        public string Subtitle = string.Empty;
        public readonly List<string> Lines = new List<string>();

        /// <param name="runCgCount">이번 실행에서 본 CG 수(저장·불러오기를 넘어서는 값은 추적하지 않는다).</param>
        /// <param name="totalCgCount">프로필에 누적된 본 CG 수(없으면 -1).</param>
        /// <param name="reachedCount">프로필에 기록된 도달 엔딩 수(없으면 -1).</param>
        /// <param name="totalEndings">도달 가능한 엔딩 칸 수.</param>
        public static EndSummary Build(EndingPlan plan, GameState state, int runCgCount, int totalCgCount, int reachedCount, int totalEndings)
        {
            if (plan == null) throw new ArgumentNullException("plan");
            if (state == null) throw new ArgumentNullException("state");
            EndSummary s = new EndSummary();
            s.Title = EndingPlan.TitleOf(plan.Kind);
            switch (plan.Kind)
            {
                case EndingKind.True:
                case EndingKind.Good:
                case EndingKind.Normal:
                    s.Subtitle = Loc.F("end_sub_single", "{0}과(와)의 이야기", plan.PrimaryName);
                    break;
                case EndingKind.HaremDuo:
                case EndingKind.HaremTrio:
                    s.Subtitle = Loc.F("end_sub_group", "{0}과(와) 함께", string.Join(", ", plan.MemberNames.ToArray()));
                    break;
                default:
                    s.Subtitle = Loc.T("end_sub_community", "모두와 함께하는 달빛 모퉁이");
                    break;
            }
            int days = Math.Max(0, state.Day - 1);
            s.Lines.Add(Loc.F("end_days_chapters", "{0}일을 달려 {1}챕터를 완주했습니다.", days, state.Chapter));
            s.Lines.Add(Loc.F("end_final_stats", "최종 골드 {0} G · 가게 레벨 {1} · 평판 {2}", state.Gold.ToString("N0"), state.ShopLevel, state.Reputation.ToString("F0")));
            s.Lines.Add(Loc.F("end_play_time", "플레이 시간 {0}", FormatPlayTime(state.PlayTimeSec)));
            s.Lines.Add(Loc.F("end_cg_seen", "이번에 본 CG {0}장", runCgCount) + (totalCgCount >= 0 ? Loc.F("end_cg_total", " (누적 {0}장)", totalCgCount) : string.Empty));
            if (reachedCount >= 0 && totalEndings > 0) s.Lines.Add(Loc.F("end_reached", "도달한 엔딩 {0} / {1}", reachedCount, totalEndings));
            return s;
        }

        /// <summary>초 → "H시간 M분" 또는 "M분".</summary>
        public static string FormatPlayTime(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0.0) seconds = 0.0;
            int totalMinutes = (int)(seconds / 60.0);
            int h = totalMinutes / 60;
            int m = totalMinutes % 60;
            return h > 0 ? Loc.F("playtime_hm", "{0}시간 {1}분", h, m) : Loc.F("playtime_m", "{0}분", m);
        }
    }
}
