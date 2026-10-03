using System;
using System.Collections.Generic;
using MorpheHouse.Data;
using MorpheHouse.Romance;

namespace MorpheHouse.GameFlow
{
    /// <summary>
    /// 게임 종료 시 UI 가 한 번 호출하는 엔딩 계획: 판정 결과 + 재생할 노드 + 대체 문구 + 해금할 CG.
    /// 판정은 EndingEvaluator(순수)가 하고, 이 클래스는 노드 존재 여부와 표시 문구만 덧붙인다(UI 비의존).
    /// </summary>
    public sealed class EndingPlan
    {
        public EndingResult Result;
        /// <summary>재생할 노드. 시나리오에 노드가 없으면 null(대체 문구 사용).</summary>
        public string NodeName;
        /// <summary>원래 재생하려던 노드 이름(노드 유무와 무관). 로그·QA 용.</summary>
        public string IntendedNode = string.Empty;
        public string TitleKo = string.Empty;
        /// <summary>노드가 없을 때 보여 줄 짧은 문구.</summary>
        public string FallbackText = string.Empty;
        /// <summary>대표 인물 이름(없으면 빈 문자열). 공동 루트면 구성원 이름 전체는 MemberNames.</summary>
        public string PrimaryName = string.Empty;
        public readonly List<string> MemberNames = new List<string>();
        public readonly List<string> CgIds = new List<string>();

        public EndingKind Kind { get { return Result.Kind; } }

        /// <summary>
        /// 엔딩 계획을 만든다. nodeExists 가 null 이면 모든 노드가 있다고 본다. catalog 는 이름 표시용(null 허용).
        /// </summary>
        public static EndingPlan Create(GameState state, EndingTable table, RomanceCatalog catalog, Func<string, bool> nodeExists)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (table == null) throw new ArgumentNullException("table");

            EndingResult result = EndingEvaluator.Evaluate(state, table);
            EndingPlan plan = new EndingPlan();
            plan.Result = result;
            plan.IntendedNode = result.NodeName;
            plan.NodeName = (nodeExists == null || nodeExists(result.NodeName)) ? result.NodeName : null;
            for (int i = 0; i < result.CgIds.Count; i++) plan.CgIds.Add(result.CgIds[i]);
            for (int i = 0; i < result.Members.Count; i++) plan.MemberNames.Add(NameOf(catalog, result.Members[i]));
            plan.PrimaryName = result.PrimaryChar.Length > 0 ? NameOf(catalog, result.PrimaryChar) : string.Empty;
            plan.TitleKo = TitleOf(result.Kind);
            plan.FallbackText = FallbackOf(plan);
            return plan;
        }

        /// <summary>엔딩 종류 → 제목.</summary>
        public static string TitleOf(EndingKind kind)
        {
            switch (kind)
            {
                case EndingKind.True: return Loc.T("ending_title_true", "트루 엔딩");
                case EndingKind.Good: return Loc.T("ending_title_good", "굿 엔딩");
                case EndingKind.Normal: return Loc.T("ending_title_normal", "노멀 엔딩");
                case EndingKind.HaremDuo: return Loc.T("ending_title_duo", "두 사람과 함께하는 엔딩");
                case EndingKind.HaremTrio: return Loc.T("ending_title_trio", "세 사람과 함께하는 엔딩");
                default: return Loc.T("ending_title_community", "달빛 모퉁이");
            }
        }

        private static string FallbackOf(EndingPlan p)
        {
            switch (p.Result.Kind)
            {
                case EndingKind.True:
                case EndingKind.Good:
                case EndingKind.Normal:
                    return Loc.F("ending_fallback_single", "({0}과(와)의 엔딩 시나리오는 준비 중입니다.) 달빛 모퉁이의 불빛은 오늘도 따뜻하다.", p.PrimaryName);
                case EndingKind.HaremDuo:
                case EndingKind.HaremTrio:
                    return Loc.F("ending_fallback_group", "({0}과(와) 함께하는 엔딩 시나리오는 준비 중입니다.) 서로의 마음을 확인한 밤이 지나간다.", string.Join(", ", p.MemberNames.ToArray()));
                default:
                    return Loc.T("ending_fallback_community", "(공통 엔딩 시나리오는 준비 중입니다.) 모두가 모인 달빛 모퉁이에는 오늘도 웃음이 끊이지 않는다.");
            }
        }

        private static string NameOf(RomanceCatalog catalog, string id)
        {
            CharacterRow row = catalog == null ? null : catalog.FindCharacter(id);
            return Loc.CharName(id, row != null ? row.NameKo : id);
        }
    }
}
