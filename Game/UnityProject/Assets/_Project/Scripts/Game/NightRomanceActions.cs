using System;
using System.Collections.Generic;
using MorpheHouse.Data;
using MorpheHouse.Dialogue;
using MorpheHouse.Romance;

namespace MorpheHouse.GameFlow
{
    /// <summary>밤 연애 행동 한 번의 결과: 서비스 결과 + 재생할 노드 + 대체 대사 + 화면에 띄울 요약.</summary>
    public sealed class NightActionOutcome
    {
        public ActionResult Result;
        public string Kind = string.Empty;
        public string CharId = string.Empty;
        /// <summary>재생할 노드 제목. 해당 노드가 없으면 null(대체 대사를 쓴다).</summary>
        public string NodeName;
        public string FallbackSpeaker;
        public string FallbackText = string.Empty;
        /// <summary>노드에 넘길 임시 변수($gift_name, $spot_name, $event_step ...).</summary>
        public Dictionary<string, DialogueValue> Temps = new Dictionary<string, DialogueValue>();
        /// <summary>행동 후 토스트로 보여 줄 수치 요약(성공 시).</summary>
        public string Summary = string.Empty;
        /// <summary>거절 시 안내 문구.</summary>
        public string RefusalMessage = string.Empty;

        public bool Success { get { return Result != null && Result.Success; } }
    }

    public sealed class GiftChoice
    {
        public string Id;
        public string NameKo;
        public int PriceTier;
    }

    public sealed class SpotChoice
    {
        public string Id;
        public string NameKo;
        public int UnlockChapter;
        public string Period;
        public bool Locked;
    }

    /// <summary>루트 확정 화면의 후보 한 명: 확정 가능 여부와 불가 사유(코드·문구).</summary>
    public sealed class RouteLockOption
    {
        public string Id = string.Empty;
        public string NameKo = string.Empty;
        /// <summary>단독 또는 공동 루트의 구성원으로라도 확정 가능한가(공동 기준이 더 낮으므로 SingleEligible 보다 넓다).</summary>
        public bool Eligible;
        /// <summary>혼자 확정(단독 루트) 가능한가: 호감 60 · 신뢰 20.</summary>
        public bool SingleEligible;
        /// <summary>불가 사유 코드(already_locked / chapter_too_early / affection_too_low / trust_too_low). 가능하면 빈 문자열.</summary>
        public string Reason = string.Empty;
        public string Message = string.Empty;
        public float Aff;
        public float Trust;
        /// <summary>다자 동의 이벤트(harem_ok_&lt;char&gt;)를 이미 마쳤는지. 공동 루트 엔딩 조건 안내용.</summary>
        public bool HaremConsentDone;
    }

    /// <summary>
    /// 밤 메뉴의 연애 행동(대화·선물·데이트·개인 이벤트)과 루트 확정을 묶는 순수 컨트롤러.
    /// 각 행동은 RomanceService 로 먼저 실행(행동 1회 소모)하고, 성공하면 맞는 Yarn 노드 이름을 정해 돌려준다.
    /// 노드가 없으면 NodeName=null 과 대체 대사를 채워 주므로 UI 는 막히지 않는다.
    /// 노드 이름 규칙(&lt;Char&gt; = 첫 글자 대문자 id, 예 Howl):
    ///   대화 &lt;Char&gt;_Talk_NN(01~40, 호감 구간 3등분 + 미방문 우선 + 회전), 선물 &lt;Char&gt;_Gift_Like|Neutral|Dislike,
    ///   데이트 &lt;Char&gt;_Date_&lt;SpotPascal&gt;(fish_market → FishMarket, 원본 id 도 시도) 없으면 &lt;Char&gt;_Date_Generic, 이벤트 &lt;Char&gt;_E&lt;step&gt;.
    /// </summary>
    public sealed class NightRomanceActions
    {
        public const int MaxTalkIndex = 40;

        /// <summary>이벤트 조건은 충족했지만 장면 게이트가 닫혀 있을 때의 안내(UI 가 빈 라벨을 보이지 않도록). 선행 장면 이름을 밝히지 않는다.</summary>
        public const string EventNotYetText = "아직 때가 아니다";

        private readonly GameState _state;
        private readonly BalanceTable _balance;
        private readonly RomanceService _romance;
        private readonly RomanceCatalog _catalog;
        private readonly Func<string, bool> _nodeExists;
        private readonly Func<string, bool> _hasVisited;

        public NightRomanceActions(GameState state, BalanceTable balance, RomanceService romance, RomanceCatalog catalog,
            Func<string, bool> nodeExists, Func<string, bool> hasVisited)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (balance == null) throw new ArgumentNullException("balance");
            if (romance == null) throw new ArgumentNullException("romance");
            _state = state;
            _balance = balance;
            _romance = romance;
            _catalog = catalog;
            _nodeExists = nodeExists ?? delegate (string n) { return false; };
            _hasVisited = hasVisited ?? delegate (string n) { return false; };
            // 장면 게이트 자동 연결: 방문 기록을 줄 수 있고 카탈로그에 scene_rules 가 있을 때만(없으면 게이트 없음 = 기존 동작).
            if (hasVisited != null && catalog != null && !string.IsNullOrEmpty(catalog.SceneRulesText))
                EnableSceneGate(SceneRuleTable.Parse(catalog.SceneRulesText));
        }

        private SceneGate _gate;

        /// <summary>장면 게이트(없으면 null). 개인 이벤트의 "볼 수 있음" 판정과 공통 장면 자동 재생이 이 게이트를 따른다.</summary>
        public SceneGate Gate { get { return _gate; } }

        /// <summary>규칙 표로 게이트를 켠다. RomanceService.EventGate 에도 연결하므로 닫힌 이벤트는 밤 메뉴·완료 모두에서 제외된다.</summary>
        public void EnableSceneGate(SceneRuleTable table)
        {
            if (table == null) { DisableSceneGate(); return; }
            _gate = new SceneGate(table, new StateSceneContext(_state, _hasVisited, _nodeExists));
            SceneGate g = _gate;
            _romance.EventGate = delegate (string node) { return g.Check(node).Allowed; };
        }

        public void DisableSceneGate()
        {
            _gate = null;
            _romance.EventGate = null;
        }

        /// <summary>
        /// 지금 자동 재생할 공통 장면 한 단계(없으면 ChapterStep.NoStep). 호스트는 아침 열 때(Morning)·밤 메뉴 열 때(Night)·행동을 다 쓴 뒤(NightEnd)
        /// 호출하고, 반환된 노드를 재생한 뒤 방문 기록이 남으면 다시 호출해 더 없을 때까지 반복한다.
        /// </summary>
        public ChapterStep NextCommonScene(SceneSlot slot)
        {
            return ChapterFlow.NextCommonScene(_gate, slot);
        }

        // ---------- 이름 ----------

        /// <summary>노드 이름에 쓰는 캐릭터 표기(howl → Howl).</summary>
        public static string CharTitle(string charId)
        {
            if (string.IsNullOrEmpty(charId)) return string.Empty;
            return char.ToUpperInvariant(charId[0]) + charId.Substring(1);
        }

        public static string TalkNodeName(string charId, int index)
        {
            return CharTitle(charId) + "_Talk_" + index.ToString("D2");
        }

        public static string GiftNodeName(string charId, Preference pref)
        {
            string suffix = pref == Preference.Like ? "Like" : (pref == Preference.Dislike ? "Dislike" : "Neutral");
            return CharTitle(charId) + "_Gift_" + suffix;
        }

        /// <summary>스팟 id → 노드 표기(fish_market → FishMarket). 시나리오 노드는 이 PascalCase 를 쓴다.</summary>
        public static string SpotTitle(string spotId)
        {
            if (string.IsNullOrEmpty(spotId)) return string.Empty;
            string[] parts = spotId.Split('_');
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;
                sb.Append(char.ToUpperInvariant(parts[i][0])).Append(parts[i].Substring(1));
            }
            return sb.ToString();
        }

        public static string DateNodeName(string charId, string spotId)
        {
            return CharTitle(charId) + "_Date_" + SpotTitle(spotId);
        }

        public static string DateGenericNodeName(string charId)
        {
            return CharTitle(charId) + "_Date_Generic";
        }

        public static string EventNodeName(string charId, int step)
        {
            return CharTitle(charId) + "_E" + step;
        }

        public string CharName(string charId)
        {
            CharacterRow row = _catalog == null ? null : _catalog.FindCharacter(charId);
            return row != null ? row.NameKo : charId;
        }

        // ---------- 조회(UI 비활성/표시용) ----------

        /// <summary>지금 볼 수 있는 개인 이벤트가 있는가(★ 표시).</summary>
        public bool HasEvent(string charId)
        {
            return _state.Phase == MorpheHouse.Core.DayPhase.Night && _romance.GetAvailableEvent(charId) != null;
        }

        /// <summary>이 인물이 이미 이야기에 등장했는지(characters.csv first_chapter). 인물 선택 목록은 false 인 인물을 숨기거나 비활성으로 보여야 한다.</summary>
        public bool IsAvailable(string charId)
        {
            return _romance.IsAvailable(charId);
        }

        /// <summary>대화·선물·데이트·이벤트 대상으로 고를 수 있는(이미 등장한) 인물 id 들(CharIds.All 순서).</summary>
        public List<string> AvailableChars()
        {
            List<string> list = new List<string>();
            for (int i = 0; i < CharIds.All.Length; i++)
                if (_romance.IsAvailable(CharIds.All[i])) list.Add(CharIds.All[i]);
            return list;
        }

        /// <summary>인물을 고를 수 없는 이유 문구(고를 수 있으면 빈 문자열). 이름·등장 챕터를 밝히지 않는 비스포일러 문구.</summary>
        public string CharUnavailableText(string charId)
        {
            return _romance.IsAvailable(charId) ? string.Empty : RefusalText(RomanceRefusal.NotMet);
        }

        /// <summary>선물을 줄 수 없는 이유 코드(줄 수 있으면 null). 서비스의 검사 순서와 같다.</summary>
        public string GiftBlockReason(string charId)
        {
            if (!_romance.IsAvailable(charId)) return RomanceRefusal.NotMet;
            if (_state.ActionsLeft <= 0) return RomanceRefusal.NoActions;
            if (_catalog == null) return RomanceRefusal.NoCatalog;
            if (_romance.IsFrozen(charId)) return null;
            if (!_romance.GiftReady(charId)) return RomanceRefusal.Cooldown;
            if (_state.Gold < _romance.GiftCost()) return RomanceRefusal.NoGold;
            return null;
        }

        public string DateBlockReason(string charId)
        {
            if (!_romance.IsAvailable(charId)) return RomanceRefusal.NotMet;
            if (_state.ActionsLeft <= 0) return RomanceRefusal.NoActions;
            if (_catalog == null) return RomanceRefusal.NoCatalog;
            if (_romance.IsFrozen(charId)) return null;
            if (!_romance.DateReady(charId)) return RomanceRefusal.Cooldown;
            if (_state.Gold < _romance.DateCost()) return RomanceRefusal.NoGold;
            return null;
        }

        /// <summary>선물 목록(gifts.csv 순서가 아니라 등급 → id 순). 인벤토리는 무시한다.</summary>
        public List<GiftChoice> GiftChoices()
        {
            List<GiftChoice> list = new List<GiftChoice>();
            if (_catalog == null) return list;
            foreach (KeyValuePair<string, GiftRow> p in _catalog.Gifts)
            {
                GiftChoice c = new GiftChoice();
                c.Id = p.Value.Id;
                c.NameKo = p.Value.NameKo;
                c.PriceTier = p.Value.PriceTier;
                list.Add(c);
            }
            list.Sort(delegate (GiftChoice a, GiftChoice b)
            {
                int t = a.PriceTier.CompareTo(b.PriceTier);
                return t != 0 ? t : string.CompareOrdinal(a.Id, b.Id);
            });
            return list;
        }

        /// <summary>데이트 스팟 목록. 현재 챕터에 해금되지 않은 곳은 Locked=true(UI 가 비활성으로 표시).</summary>
        public List<SpotChoice> SpotChoices()
        {
            List<SpotChoice> list = new List<SpotChoice>();
            if (_catalog == null) return list;
            foreach (KeyValuePair<string, SpotRow> p in _catalog.Spots)
            {
                SpotChoice c = new SpotChoice();
                c.Id = p.Value.Id;
                c.NameKo = p.Value.NameKo;
                c.UnlockChapter = p.Value.UnlockChapter;
                c.Period = p.Value.Period;
                c.Locked = p.Value.UnlockChapter > _state.Chapter;
                list.Add(c);
            }
            list.Sort(delegate (SpotChoice a, SpotChoice b)
            {
                int t = a.UnlockChapter.CompareTo(b.UnlockChapter);
                return t != 0 ? t : string.CompareOrdinal(a.Id, b.Id);
            });
            return list;
        }

        /// <summary>이벤트가 아직 열리지 않은 이유(UI 힌트). 열려 있으면 빈 문자열.</summary>
        public string EventHint(string charId)
        {
            CharState cs;
            if (!_state.Chars.TryGetValue(charId, out cs)) return string.Empty;
            if (!_romance.IsAvailable(charId)) return RefusalText(RomanceRefusal.NotMet);
            if (_romance.IsFrozen(charId)) return "루트 확정으로 닫힘";
            int step = cs.EventsDone + 1;
            EventRow row = null;
            for (int i = 0; i < _balance.Events.Count; i++)
                if (_balance.Events[i].Step == step) { row = _balance.Events[i]; break; }
            if (row == null) return "모든 이벤트 완료";
            row = _romance.EffectiveRow(charId, row); // 인물별 일정(event_schedule.csv) 반영
            if (_romance.IsEventExpired(charId)) return "E" + step + " 기간 종료";
            // 조건은 충족했는데 장면 게이트(선행 장면·최소 일수)가 닫혀 있으면 스포일러 없는 안내를 돌려준다(빈 문자열 = 지금 볼 수 있음).
            if (RomanceRules.EventUnlocked(row, _state.Chapter, cs.Aff, cs.Trust))
                return _romance.GetAvailableEvent(charId) != null ? string.Empty : EventNotYetText;
            return "E" + step + " 조건: " + row.ChapterMin + "장 · 호감 " + row.AffMin.ToString("F0") + " · 신뢰 " + row.TrustMin.ToString("F0");
        }

        // ---------- 대화 ----------

        /// <summary>대화 노드 고르기(규칙은 클래스 설명). 존재하는 Talk 노드가 하나도 없으면 null.</summary>
        public string PickTalkNode(string charId)
        {
            List<int> found = new List<int>();
            for (int n = 1; n <= MaxTalkIndex; n++)
                if (_nodeExists(TalkNodeName(charId, n))) found.Add(n);
            if (found.Count == 0) return null;

            List<int> group = found;
            if (found.Count >= 3)
            {
                int band = (int)AddressBook.BandOf(_state.AffinityOf(charId));
                int start = band * found.Count / 3;
                int end = (band + 1) * found.Count / 3;
                group = found.GetRange(start, end - start);
            }
            for (int i = 0; i < group.Count; i++)
            {
                string name = TalkNodeName(charId, group[i]);
                if (!_hasVisited(name)) return name;
            }
            int counter = _romance.GetFlag(TalkCounterKey(charId));
            return TalkNodeName(charId, group[counter % group.Count]);
        }

        private static string TalkCounterKey(string charId)
        {
            return "sys_talk_" + charId;
        }

        public NightActionOutcome Talk(string charId)
        {
            string node = PickTalkNode(charId);
            NightActionOutcome o = Begin("talk", charId, _romance.Talk(charId));
            if (!o.Success) return o;
            _romance.SetFlag(TalkCounterKey(charId), _romance.GetFlag(TalkCounterKey(charId)) + 1);
            o.NodeName = node;
            o.FallbackSpeaker = charId;
            o.FallbackText = Variant(charId,
                "…오늘 하루는 어땠어? 이렇게 이야기하고 있으니 시간 가는 줄 모르겠네.",
                "별일 없었지? 가게 정리하다 보면 이런 시간이 제일 좋더라.",
                "요즘 어때. 힘든 건 없고? …그냥 궁금해서.");
            o.Summary = Summary(o);
            return o;
        }

        // ---------- 선물 ----------

        public NightActionOutcome Gift(string charId, string giftId)
        {
            GiftRow gift = _catalog == null ? null : _catalog.FindGift(giftId);
            Preference pref = _catalog == null ? Preference.Neutral : _catalog.GetPreference(charId, PrefKind.Gift, giftId);
            NightActionOutcome o = Begin("gift", charId, _romance.Gift(charId, giftId));
            if (!o.Success) return o;
            string giftName = gift != null ? gift.NameKo : giftId;
            string node = GiftNodeName(charId, pref);
            o.NodeName = _nodeExists(node) ? node : null;
            o.Temps["gift_name"] = DialogueValue.FromString(giftName);
            o.Temps["gift_pref"] = DialogueValue.FromString(pref == Preference.Like ? "like" : (pref == Preference.Dislike ? "dislike" : "neutral"));
            o.FallbackSpeaker = charId;
            if (pref == Preference.Like) o.FallbackText = "이거… 정말 마음에 들어. 고마워, 소중히 쓸게.";
            else if (pref == Preference.Dislike) o.FallbackText = "…고마워. 마음만은 잘 받았어.";
            else o.FallbackText = "고마워. 잘 쓸게.";
            o.Summary = Summary(o);
            return o;
        }

        // ---------- 데이트 ----------

        public NightActionOutcome Date(string charId, string spotId)
        {
            SpotRow spot = _catalog == null ? null : _catalog.FindSpot(spotId);
            NightActionOutcome o = Begin("date", charId, _romance.Date(charId, spotId));
            if (!o.Success) return o;
            string specific = DateNodeName(charId, spotId);
            string rawId = CharTitle(charId) + "_Date_" + spotId;
            string generic = DateGenericNodeName(charId);
            if (_nodeExists(specific)) o.NodeName = specific;
            else if (_nodeExists(rawId)) o.NodeName = rawId;
            else if (_nodeExists(generic)) o.NodeName = generic;
            else o.NodeName = null;
            o.Temps["spot_name"] = DialogueValue.FromString(spot != null ? spot.NameKo : spotId);
            o.Temps["spot_id"] = DialogueValue.FromString(spotId);
            o.FallbackSpeaker = charId;
            o.FallbackText = "오늘 즐거웠어. 다음에도… 같이 와 줄래?";
            o.Summary = Summary(o);
            return o;
        }

        // ---------- 개인 이벤트 ----------

        public NightActionOutcome Event(string charId)
        {
            if (!_romance.IsAvailable(charId))
                return Begin("event", charId, ActionResult.Refused("event", charId, RomanceRefusal.NotMet));
            EventRow e = _romance.GetAvailableEvent(charId);
            if (e == null)
                return Begin("event", charId, ActionResult.Refused("event", charId, RomanceRefusal.EventNotAvailable));
            NightActionOutcome o = Begin("event", charId, _romance.CompleteEventResult(charId, e.Step));
            if (!o.Success) return o;
            string node = EventNodeName(charId, e.Step);
            o.NodeName = _nodeExists(node) ? node : null;
            o.Temps["event_step"] = DialogueValue.FromNumber(e.Step);
            o.FallbackSpeaker = null;
            o.FallbackText = "(" + CharName(charId) + "과(와)의 이벤트 " + e.Step + "단계 시나리오는 준비 중입니다.) 함께 보낸 밤은 오래 기억에 남을 것 같다.";
            o.Summary = Summary(o);
            return o;
        }

        // ---------- 루트 확정 ----------

        /// <summary>루트 확정 가능한 인물 id 들(챕터 7+, 호감 60+, 신뢰 20+, 아직 확정 전).</summary>
        public List<string> RouteLockCandidates()
        {
            List<string> list = new List<string>();
            if (_state.HasRouteLock) return list;
            for (int i = 0; i < CharIds.All.Length; i++)
            {
                string id = CharIds.All[i];
                CharState cs;
                if (!_state.Chars.TryGetValue(id, out cs)) continue;
                if (RomanceRules.CanLockRoute(_state.Chapter, cs.Aff, cs.Trust)
                    || (_state.Chapter >= RomanceRules.LockMinChapter && cs.Aff >= _romance.GroupLockMinAff(2) && cs.Trust >= _romance.GroupLockMinTrust(2))) list.Add(id); // 공동 루트 기준 포함
            }
            return list;
        }

        public bool TryLockRoute(string charId, out string message)
        {
            string reason;
            if (_romance.TryLockRoute(charId, out reason))
            {
                message = CharName(charId) + "과(와)의 길을 선택했습니다. 다른 인물과의 관계는 더 이상 변하지 않습니다.";
                return true;
            }
            switch (reason)
            {
                case "already_locked": message = "이미 루트가 확정되었습니다."; break;
                case "chapter_too_early": message = "루트 확정은 " + RomanceRules.LockMinChapter + "장부터 가능합니다."; break;
                case "affection_too_low": message = "호감도가 부족합니다. (필요 " + RomanceRules.LockMinAff.ToString("F0") + ")"; break;
                case "trust_too_low": message = "신뢰도가 부족합니다. (필요 " + RomanceRules.LockMinTrust.ToString("F0") + ")"; break;
                default: message = "루트를 확정할 수 없습니다."; break;
            }
            return false;
        }

        /// <summary>
        /// 루트 확정 화면용 후보 목록(공략 대상 6명 전원). 확정 가능 여부와 거절 사유를 개별로 채운다.
        /// 인물 한 명 기준 조건(챕터 7+, 호감 60+, 신뢰 20+, 확정 전)만 본다. 인원수·조합 검증은 <see cref="ValidateGroup"/>.
        /// </summary>
        public List<RouteLockOption> RouteLockOptions()
        {
            List<RouteLockOption> list = new List<RouteLockOption>();
            for (int i = 0; i < CharIds.All.Length; i++)
            {
                string id = CharIds.All[i];
                RouteLockOption o = new RouteLockOption();
                o.Id = id;
                o.NameKo = CharName(id);
                CharState cs;
                if (_state.Chars.TryGetValue(id, out cs)) { o.Aff = cs.Aff; o.Trust = cs.Trust; }
                o.HaremConsentDone = _romance.HasFlag("harem_ok_" + id);
                o.Reason = _romance.CheckGroupLock(new string[] { id }) ?? string.Empty;
                o.SingleEligible = o.Reason.Length == 0;
                o.Eligible = o.SingleEligible;
                if (!o.SingleEligible && (o.Reason == "affection_too_low" || o.Reason == "trust_too_low") && cs != null
                    && cs.Aff >= _romance.GroupLockMinAff(2) && cs.Trust >= _romance.GroupLockMinTrust(2))
                {
                    o.Eligible = true; // 공동 루트 구성원으로는 가능(단독으로는 부족)
                    o.Reason = string.Empty;
                }
                else if (!o.Eligible && cs != null && o.Reason == "affection_too_low") o.Message = LockRefusalText(o.Reason, 2);
                else if (!o.Eligible && cs != null && o.Reason == "trust_too_low") o.Message = LockRefusalText(o.Reason, 2);
                if (!o.Eligible && o.Message.Length == 0) o.Message = LockRefusalText(o.Reason);
                list.Add(o);
            }
            return list;
        }

        /// <summary>선택한 인물 묶음(1~3명)으로 확정할 수 있는지 상태 변경 없이 확인. 가능하면 true 와 빈 message, 아니면 사유 문구.</summary>
        public bool ValidateGroup(IList<string> ids, out string message)
        {
            string reason = _romance.CheckGroupLock(ids);
            message = reason == null ? string.Empty : LockRefusalText(reason, ids == null ? 1 : ids.Count);
            return reason == null;
        }

        /// <summary>
        /// 공동(다자) 루트 확정(1명이면 단독과 같다). 성공 시 구성원 이름을 담은 안내를, 실패 시 거절 문구를 message 에 돌려준다.
        /// 공동 루트의 엔딩 조건(동의 이벤트·호감/신뢰 기준)은 확정 때 검사하지 않고 EndingEvaluator 가 마지막에 판정한다.
        /// </summary>
        public bool TryLockGroup(IList<string> ids, out string message)
        {
            string reason;
            if (_romance.TryLockGroup(ids, out reason))
            {
                List<string> names = new List<string>();
                for (int i = 0; i < ids.Count; i++) names.Add(CharName(ids[i]));
                message = ids.Count == 1
                    ? CharName(ids[0]) + "과(와)의 길을 선택했습니다. 다른 인물과의 관계는 더 이상 변하지 않습니다."
                    : string.Join(", ", names.ToArray()) + "과(와) 함께하는 길을 선택했습니다. 나머지 인물과의 관계는 더 이상 변하지 않습니다.";
                return true;
            }
            message = LockRefusalText(reason, ids == null ? 1 : ids.Count);
            return false;
        }

        /// <summary>
        /// 묶음 크기에 맞는 확정 거절 문구: 호감·신뢰 부족은 2~3명이면 공동 기준(params.csv group_lock_min_*), 1명이면 단독 기준(60/20)을 보여 준다.
        /// </summary>
        public string LockRefusalText(string reason, int memberCount)
        {
            if (memberCount >= 2 && reason == "affection_too_low") return "호감도가 부족합니다. (필요 " + _romance.GroupLockMinAff(memberCount).ToString("F0") + ")";
            if (memberCount >= 2 && reason == "trust_too_low") return "신뢰도가 부족합니다. (필요 " + _romance.GroupLockMinTrust(memberCount).ToString("F0") + ")";
            return LockRefusalText(reason);
        }

        /// <summary>루트 확정 거절 사유 코드 → 안내 문구(단독 기준).</summary>
        public static string LockRefusalText(string reason)
        {
            switch (reason)
            {
                case "group_empty": return "함께할 인물을 선택해 주세요.";
                case "group_too_large": return "함께할 수 있는 인물은 최대 " + RomanceRules.MaxGroupSize + "명입니다.";
                case "duplicate_member": return "같은 인물을 두 번 선택할 수 없습니다.";
                case RomanceRefusal.UnknownChar: return "알 수 없는 인물입니다.";
                case "already_locked": return "이미 루트가 확정되었습니다.";
                case "chapter_too_early": return "루트 확정은 " + RomanceRules.LockMinChapter + "장부터 가능합니다.";
                case "affection_too_low": return "호감도가 부족합니다. (필요 " + RomanceRules.LockMinAff.ToString("F0") + ")";
                case "trust_too_low": return "신뢰도가 부족합니다. (필요 " + RomanceRules.LockMinTrust.ToString("F0") + ")";
                default: return "루트를 확정할 수 없습니다.";
            }
        }

        // ---------- 문구 ----------

        /// <summary>거절 사유 코드 → 안내 문구.</summary>
        public static string RefusalText(string reason)
        {
            switch (reason)
            {
                case RomanceRefusal.NoActions: return "오늘 밤은 더 이상 행동할 수 없습니다.";
                case RomanceRefusal.Cooldown: return "아직 쿨다운 중입니다.";
                case RomanceRefusal.NoGold: return "골드가 부족합니다.";
                case RomanceRefusal.SpotLocked: return "아직 해금되지 않은 장소입니다.";
                case RomanceRefusal.NotMet: return "아직 만나지 못한 인물입니다.";
                case RomanceRefusal.EventNotAvailable: return "지금은 볼 수 있는 이벤트가 없습니다.";
                case RomanceRefusal.WrongPhase: return "지금은 할 수 없는 행동입니다.";
                case RomanceRefusal.NoCatalog: return "선물·데이트 데이터를 불러오지 못했습니다.";
                default: return "행동할 수 없습니다. (" + reason + ")";
            }
        }

        // ---------- 내부 ----------

        private NightActionOutcome Begin(string kind, string charId, ActionResult result)
        {
            NightActionOutcome o = new NightActionOutcome();
            o.Kind = kind;
            o.CharId = charId;
            o.Result = result;
            if (!result.Success) o.RefusalMessage = RefusalText(result.Reason);
            return o;
        }

        private string Summary(NightActionOutcome o)
        {
            ActionResult r = o.Result;
            string s = CharName(o.CharId);
            if (r.Frozen)
            {
                return s + ": 루트가 확정되어 관계에 변화가 없습니다.";
            }
            s += "  호감 " + Signed(r.AffDelta) + "  신뢰 " + Signed(r.TrustDelta);
            if (r.GoldSpent > 0f) s += "  (-" + r.GoldSpent.ToString("N0") + " G)";
            return s;
        }

        private static string Signed(float v)
        {
            return (v >= 0f ? "+" : string.Empty) + v.ToString("F1");
        }

        private string Variant(string charId, string a, string b, string c)
        {
            int idx = Array.IndexOf(CharIds.All, charId);
            int k = (_state.Day + (idx < 0 ? 0 : idx)) % 3;
            return k == 0 ? a : (k == 1 ? b : c);
        }
    }
}
