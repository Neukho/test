using System;
using System.Collections.Generic;
using MorpheHouse.Data;
using MorpheHouse.Romance;

namespace MorpheHouse.GameFlow
{
    /// <summary>재생할 장면 한 칸: 노드(없으면 대체 문구).</summary>
    public sealed class SceneItem
    {
        /// <summary>실제로 재생할 노드. 시나리오에 없으면 null(대체 문구 사용).</summary>
        public string Node;
        /// <summary>원래 재생하려던 노드 이름(로그·QA 용).</summary>
        public string IntendedNode = string.Empty;
        public string FallbackSpeaker;
        public string FallbackText = string.Empty;

        public static SceneItem Of(string intended, bool exists, string fallbackText)
        {
            SceneItem s = new SceneItem();
            s.IntendedNode = intended ?? string.Empty;
            s.Node = exists ? intended : null;
            s.FallbackText = fallbackText ?? string.Empty;
            return s;
        }
    }

    /// <summary>루트 선택 화면 상태: 아직 정하지 않음 / 단독 / 공동.</summary>
    public enum RouteChoiceMode
    {
        Undecided = 0,
        Single = 1,
        Group = 2
    }

    /// <summary>
    /// 루트 선택 화면의 순수 모델. 후보 6명(호감·신뢰·적격 여부·거절 사유), 선택(최대 3명), 확정 검증·실행을 담당하고
    /// 실제 규칙은 NightRomanceActions(RouteLockOptions / ValidateGroup / TryLockGroup)에 위임한다. UI 는 이 모델의 상태를 그리기만 한다.
    /// </summary>
    public sealed class RouteSelectModel
    {
        public const int MaxMembers = 3;

        private readonly NightRomanceActions _night;
        private readonly EndingTable _table;
        private readonly List<string> _selected = new List<string>();
        private List<RouteLockOption> _options;

        public RouteSelectModel(NightRomanceActions night, EndingTable table)
        {
            if (night == null) throw new ArgumentNullException("night");
            _night = night;
            _table = table;
            Refresh();
        }

        /// <summary>후보 목록(공략 대상 6명 전원, CharIds 순서).</summary>
        public List<RouteLockOption> Options { get { return _options; } }

        /// <summary>선택한 인물 id(선택한 순서).</summary>
        public List<string> Selected { get { return _selected; } }

        /// <summary>후보를 다시 읽는다(선택은 유지하되 더는 적격이 아닌 인물은 뺀다).</summary>
        public void Refresh()
        {
            _options = _night.RouteLockOptions();
            for (int i = _selected.Count - 1; i >= 0; i--)
            {
                RouteLockOption o = Find(_selected[i]);
                if (o == null || !o.Eligible) _selected.RemoveAt(i);
            }
        }

        public RouteLockOption Find(string id)
        {
            for (int i = 0; i < _options.Count; i++) if (_options[i].Id == id) return _options[i];
            return null;
        }

        public bool IsSelected(string id)
        {
            return _selected.Contains(id);
        }

        /// <summary>한 명이라도 확정 가능한 인물이 있는가.</summary>
        public bool AnyEligible
        {
            get
            {
                for (int i = 0; i < _options.Count; i++) if (_options[i].Eligible) return true;
                return false;
            }
        }

        public RouteChoiceMode Mode
        {
            get { return _selected.Count == 0 ? RouteChoiceMode.Undecided : (_selected.Count == 1 ? RouteChoiceMode.Single : RouteChoiceMode.Group); }
        }

        /// <summary>
        /// 선택을 토글한다. 이미 선택했으면 해제(항상 성공). 적격이 아니거나 4번째 선택이면 거절하고 한국어 사유를 message 에 돌려준다.
        /// </summary>
        public bool Toggle(string id, out string message)
        {
            message = string.Empty;
            if (_selected.Remove(id)) return true;
            RouteLockOption o = Find(id);
            if (o == null)
            {
                message = NightRomanceActions.LockRefusalText(RomanceRefusal.UnknownChar);
                return false;
            }
            if (!o.Eligible)
            {
                message = o.Message.Length > 0 ? o.Message : NightRomanceActions.LockRefusalText(o.Reason);
                return false;
            }
            if (_selected.Count >= MaxMembers)
            {
                message = NightRomanceActions.LockRefusalText("group_too_large");
                return false;
            }
            _selected.Add(id);
            return true;
        }

        public void ClearSelection()
        {
            _selected.Clear();
        }

        /// <summary>현재 선택으로 확정할 수 있는가(상태 변경 없음). 못 하면 사유 문구.</summary>
        public bool CanConfirm(out string message)
        {
            return _night.ValidateGroup(_selected, out message);
        }

        /// <summary>확정 실행. 성공하면 <see cref="LockedMembers"/> 에 구성원이 남는다. 실패해도 예외 없이 사유 문구.</summary>
        public bool Commit(out string message)
        {
            List<string> members = new List<string>(_selected);
            bool ok = _night.TryLockGroup(members, out message);
            LockedMembers.Clear();
            if (ok) LockedMembers.AddRange(members);
            Refresh();
            return ok;
        }

        /// <summary>마지막 <see cref="Commit"/> 이 성공했을 때의 구성원(그 외에는 빈 목록).</summary>
        public readonly List<string> LockedMembers = new List<string>();

        /// <summary>선택 인원에 맞는 짧은 설명(단독/공동/미선택).</summary>
        public string ModeExplanation()
        {
            switch (Mode)
            {
                case RouteChoiceMode.Single:
                    return Loc.T("route_mode_single", "단독 루트: 선택한 한 사람과의 이야기가 이어집니다. 노멀 · 굿 · 트루 엔딩이 있습니다.");
                case RouteChoiceMode.Group:
                    return GroupRulesText(_table, _selected.Count);
                default:
                    return Loc.F("route_mode_undecided", "한 명을 고르면 단독 루트, 두세 명을 고르면 공동 루트입니다. 최대 {0}명까지 고를 수 있습니다.", MaxMembers);
            }
        }

        /// <summary>
        /// 공동 루트 요건 설명. 수치는 endings.csv(harem_duo / harem_trio)에서 읽고, 표가 없으면 수치 없이 일반 설명만 돌려준다.
        /// </summary>
        public static string GroupRulesText(EndingTable table, int size)
        {
            string head = Loc.F("route_group_head", "공동 루트({0}명): 모든 구성원이 서로 알고 동의하는 관계로만 그려지며, 구성원 전원이 각자 동의 장면에서 동의해야 합니다. ", size);
            EndingRow row = table == null ? null : (size >= 3 ? table.HaremTrioRow : table.HaremDuoRow);
            string need = row == null
                ? Loc.T("route_group_need_generic", "엔딩 조건은 구성원 전원이 충족해야 합니다. ")
                : Loc.F("route_group_need", "엔딩 조건(전원): 호감 {0} · 신뢰 {1} · 개인 이벤트 {2}단계 이상. ", row.AffMin.ToString("F0"), row.TrustMin.ToString("F0"), row.RequiredEvents);
            return head + need + Loc.T("route_group_tail", "한 명이라도 부족하거나 동의하지 않으면 커뮤니티 엔딩으로 이어집니다.");
        }

        /// <summary>확정 직전 경고(비구성원이 동결됨).</summary>
        public string LockWarning()
        {
            return Loc.T("route_lock_warning", "확정하면 되돌릴 수 없고, 선택하지 않은 인물과의 관계는 더 이상 변하지 않습니다(성장 정지).");
        }

        /// <summary>
        /// 후보 한 줄의 공동 루트 기준 안내(호감·신뢰·이벤트가 공동 기준을 채웠는지). 표가 없으면 빈 문자열.
        /// 이벤트 단계는 후보 모델에 없으므로 호감·신뢰만 본다.
        /// </summary>
        public string ReadinessHint(string id, int groupSize)
        {
            RouteLockOption o = Find(id);
            if (o == null || _table == null) return string.Empty;
            EndingRow row = groupSize >= 3 ? _table.HaremTrioRow : _table.HaremDuoRow;
            bool ok = o.Aff >= row.AffMin && o.Trust >= row.TrustMin;
            return ok ? Loc.T("route_ready_ok", "공동 기준 충족") : Loc.F("route_ready_short", "공동 기준 미달(호감 {0} · 신뢰 {1})", row.AffMin.ToString("F0"), row.TrustMin.ToString("F0"));
        }
    }

    /// <summary>루트 확정 전후 흐름 규칙(순수): 확정 직후 장면, 자동 안내 조건, 메뉴 항목 노출 조건.</summary>
    public static class RouteFlow
    {
        public const string HaremTable = "Harem_Table";
        /// <summary>"정하지 않고 계속" 을 고른 뒤 자동 안내를 멈추는 호스트 전용 플래그(시나리오는 읽지 않는다).</summary>
        public const string DeclinedFlag = "sys_route_declined";

        public static string EpilogueNode(string charId)
        {
            return NightRomanceActions.CharTitle(charId) + "_Harem_Epilogue";
        }

        /// <summary>
        /// 공동(2명 이상) 확정 직후 재생할 장면: Harem_Table(있으면) → 구성원 순서대로 &lt;Char&gt;_Harem_Consent(있으면). 없는 노드는 건너뛴다.
        /// 단독(1명)이거나 구성원이 없으면 빈 목록. 호스트는 동의 플래그를 설정하지 않는다(노드가 설정).
        /// </summary>
        public static List<SceneItem> PostLockScenes(IList<string> members, Func<string, bool> nodeExists)
        {
            List<SceneItem> list = new List<SceneItem>();
            if (members == null || members.Count < 2 || nodeExists == null) return list;
            if (nodeExists(HaremTable)) list.Add(SceneItem.Of(HaremTable, true, string.Empty));
            for (int i = 0; i < members.Count; i++)
            {
                string node = ConsentRetry.ConsentNode(members[i]);
                if (nodeExists(node)) list.Add(SceneItem.Of(node, true, string.Empty));
            }
            return list;
        }

        /// <summary>루트 확정 메뉴 항목을 보여 줄지: 7장 이상, 확정 전, 게임 진행 중(7~8장 밤 메뉴).</summary>
        public static bool MenuEntryVisible(GameState state)
        {
            return state != null && !state.Finished && !state.HasRouteLock && state.Chapter >= RomanceRules.LockMinChapter;
        }

        /// <summary>메뉴 항목에 ★ 를 붙일지: 확정할 수 있는 인물이 있을 때.</summary>
        public static bool MenuEntryHighlighted(GameState state, int eligibleCount)
        {
            return MenuEntryVisible(state) && eligibleCount > 0;
        }

        /// <summary>
        /// 밤 시작 시 자동으로 루트 선택 화면을 띄울지: 메뉴 조건 + 적격 후보 있음 + 오늘 아직 안 띄움 + "정하지 않고 계속" 을 고르지 않음.
        /// </summary>
        public static bool ShouldAutoPrompt(GameState state, int eligibleCount, int lastPromptDay)
        {
            if (!MenuEntryVisible(state) || eligibleCount <= 0) return false;
            if (lastPromptDay == state.Day) return false;
            return !RecoveryTable.IsSet(state, DeclinedFlag);
        }

        /// <summary>대사의 route_choice 요청을 받아들일지: 아직 확정 전이고 게임이 끝나지 않았을 때.</summary>
        public static bool AcceptDialogueRequest(GameState state)
        {
            return state != null && !state.Finished && !state.HasRouteLock;
        }
    }
}
