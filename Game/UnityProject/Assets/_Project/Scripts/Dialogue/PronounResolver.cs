using System;

namespace MorpheHouse.Dialogue
{
    /// <summary>
    /// 주인공 성별·로케일에 따른 대명사/호칭 함수 제공자. 스크립트에서 쓸 수 있는 함수:
    /// pronoun_subj() pronoun_obj() pronoun_poss() honorific() name() player_name()
    /// calls_player("howl") player_calls("howl") affinity_band("howl").
    /// `{name}` 처럼 '$' 없이 식별자만 써도 인자 없는 함수 호출로 평가되므로 `{pronoun_subj}` 도 동작한다.
    /// 한국어는 성별 대명사를 쓰지 않고(이름/당신), 영어는 he/she/they 로 매핑한다.
    /// 영어 they 는 동사 일치(they are)를 자동으로 바꾸지 않으므로 작가가 문장 구조로 피한다.
    /// </summary>
    public sealed class PronounResolver : IDialogueFunctionProvider
    {
        private readonly Func<string> _gender;
        private readonly Func<string> _name;
        private readonly string _locale;
        private readonly Func<string, float> _affinity;
        private readonly AddressBook _book;

        /// <param name="gender">"male"|"female"|"nonbinary"|"neutral"(그 외 값은 neutral 로 취급).</param>
        /// <param name="locale">"ko"|"en"(en 으로 시작하면 영어, 그 외 한국어).</param>
        /// <param name="playerName">주인공 이름.</param>
        public PronounResolver(string gender, string locale, string playerName)
            : this(delegate { return gender; }, locale, delegate { return playerName; }, null, null)
        {
        }

        /// <summary>GameState 를 매 호출마다 읽는 버전(성별·이름이 바뀌어도 반영). 호칭 표와 호감도 조회를 함께 연결한다.</summary>
        public PronounResolver(MorpheHouse.Data.GameState state, string locale, AddressBook book)
            : this(delegate { return state.Gender; }, locale, delegate { return state.PlayerName; },
                   delegate (string id) { return state.AffinityOf(id); }, book)
        {
        }

        public PronounResolver(Func<string> gender, string locale, Func<string> playerName, Func<string, float> affinityOf, AddressBook book)
        {
            _gender = gender;
            _name = playerName;
            _locale = locale;
            _affinity = affinityOf;
            _book = book;
        }

        /// <summary>성별 문자열을 male/female/nonbinary/neutral 로 정규화(알 수 없으면 neutral).</summary>
        public static string NormalizeGender(string gender)
        {
            string g = (gender ?? string.Empty).Trim().ToLowerInvariant();
            switch (g)
            {
                case "male":
                case "m":
                case "man":
                    return "male";
                case "female":
                case "f":
                case "woman":
                    return "female";
                case "nonbinary":
                case "non-binary":
                case "non_binary":
                case "nb":
                case "enby":
                    return "nonbinary";
                default:
                    return "neutral";
            }
        }

        /// <summary>로케일을 ko/en 으로 정규화.</summary>
        public static string NormalizeLocale(string locale)
        {
            string l = (locale ?? string.Empty).Trim().ToLowerInvariant();
            return l.StartsWith("en") ? "en" : "ko";
        }

        public string Gender { get { return NormalizeGender(_gender()); } }

        public string Locale { get { return NormalizeLocale(_locale); } }

        public string PlayerName
        {
            get
            {
                string n = _name();
                if (!string.IsNullOrEmpty(n)) return n;
                return Locale == "en" ? "you" : "당신";
            }
        }

        public string Subject()
        {
            if (Locale == "ko") return PlayerName;
            switch (Gender)
            {
                case "male": return "he";
                case "female": return "she";
                default: return "they";
            }
        }

        public string Object()
        {
            if (Locale == "ko") return PlayerName;
            switch (Gender)
            {
                case "male": return "him";
                case "female": return "her";
                default: return "them";
            }
        }

        public string Possessive()
        {
            if (Locale == "ko") return PlayerName + "의";
            switch (Gender)
            {
                case "male": return "his";
                case "female": return "her";
                default: return "their";
            }
        }

        /// <summary>이름을 포함한 호칭. 한국어 "이름 씨"(성별 무관), 영어 Mr./Ms./Mx.</summary>
        public string Honorific()
        {
            if (Locale == "ko") return PlayerName + " 씨";
            switch (Gender)
            {
                case "male": return "Mr. " + PlayerName;
                case "female": return "Ms. " + PlayerName;
                default: return "Mx. " + PlayerName;
            }
        }

        private float AffinityOf(string charId)
        {
            return _affinity == null ? 0f : _affinity(charId);
        }

        /// <summary>호감도로 정한 구간.</summary>
        public AffinityBand BandFor(string charId)
        {
            return AddressBook.BandOf(AffinityOf(charId));
        }

        /// <summary>NPC 가 주인공을 부르는 말. 표에 없으면 "{이름} 씨"(ko) / 이름(en).</summary>
        public string CallsPlayer(string charId)
        {
            AddressTerms t = _book == null ? null : _book.Find(charId, Gender, BandFor(charId));
            string term;
            if (Locale == "ko") term = t != null && t.CallsPlayerKo.Length > 0 ? t.CallsPlayerKo : "{name} 씨";
            else term = t != null && t.CallsPlayerEn.Length > 0 ? t.CallsPlayerEn : "{name}";
            return term.Replace("{name}", PlayerName);
        }

        /// <summary>주인공이 NPC 를 부르는 말. 표에 없으면 캐릭터 id 그대로.</summary>
        public string PlayerCalls(string charId)
        {
            AddressTerms t = _book == null ? null : _book.Find(charId, Gender, BandFor(charId));
            string term = Locale == "ko" ? (t == null ? null : t.PlayerCallsKo) : (t == null ? null : t.PlayerCallsEn);
            if (string.IsNullOrEmpty(term)) return charId ?? string.Empty;
            return term.Replace("{name}", PlayerName);
        }

        public bool TryCall(string name, DialogueValue[] args, out DialogueValue result)
        {
            string s = null;
            switch (name)
            {
                case "pronoun_subj": s = Subject(); break;
                case "pronoun_obj": s = Object(); break;
                case "pronoun_poss": s = Possessive(); break;
                case "honorific": s = Honorific(); break;
                case "name":
                case "player_name": s = PlayerName; break;
                case "calls_player": s = CallsPlayer(OneArg(name, args)); break;
                case "player_calls": s = PlayerCalls(OneArg(name, args)); break;
                case "affinity_band": s = AddressBook.BandName(BandFor(OneArg(name, args))); break;
            }
            if (s == null)
            {
                result = default(DialogueValue);
                return false;
            }
            result = DialogueValue.FromString(s);
            return true;
        }

        private static string OneArg(string fn, DialogueValue[] args)
        {
            if (args.Length != 1) throw new DialogueRuntimeException("함수 " + fn + "() 는 캐릭터 id 인자 1개가 필요합니다.");
            return args[0].AsString;
        }
    }
}
