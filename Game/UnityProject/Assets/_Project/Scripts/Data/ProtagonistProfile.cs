using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MorpheHouse.Data
{
    /// <summary>주인공 생성 화면의 결과물. 검증은 <see cref="ProfileValidator"/> 가 한다.</summary>
    public sealed class ProtagonistProfile
    {
        public const string DefaultName = "주인공";

        public string Name = DefaultName;
        /// <summary>male | female | nonbinary | neutral</summary>
        public string Gender = "neutral";
        /// <summary>human | wolf | fox | tiger | rabbit | bear | cat</summary>
        public string Species = "human";
        /// <summary>외형 프리셋 1~6.</summary>
        public int Appearance = 1;

        public static ProtagonistProfile Default()
        {
            return new ProtagonistProfile();
        }

        public ProtagonistProfile Clone()
        {
            ProtagonistProfile p = new ProtagonistProfile();
            p.Name = Name;
            p.Gender = Gender;
            p.Species = Species;
            p.Appearance = Appearance;
            return p;
        }

        /// <summary>검증이 끝난 프로필을 게임 상태에 적용한다.</summary>
        public void ApplyTo(GameState state)
        {
            if (state == null) throw new ArgumentNullException("state");
            state.PlayerName = Name;
            state.Gender = Gender;
            state.PlayerSpecies = Species;
            state.PlayerAppearance = Appearance;
        }
    }

    /// <summary>검증 결과. 유효하면 Profile 에 정리된(트림·공백 정규화) 값이 들어 있다.</summary>
    public sealed class ProfileValidation
    {
        public bool IsValid;
        public string NameError = string.Empty;
        public string GenderError = string.Empty;
        public string SpeciesError = string.Empty;
        public string AppearanceError = string.Empty;
        public ProtagonistProfile Profile;

        /// <summary>첫 번째 오류의 한국어 안내(없으면 빈 문자열).</summary>
        public string FirstMessage
        {
            get
            {
                if (NameError.Length > 0) return ProfileValidator.MessageFor(NameError);
                if (GenderError.Length > 0) return ProfileValidator.MessageFor(GenderError);
                if (SpeciesError.Length > 0) return ProfileValidator.MessageFor(SpeciesError);
                if (AppearanceError.Length > 0) return ProfileValidator.MessageFor(AppearanceError);
                return string.Empty;
            }
        }
    }

    /// <summary>주인공 이름·성별·종·외형 규칙. 허용 id 목록과 한국어 라벨도 이곳이 단일 출처다.</summary>
    public static class ProfileValidator
    {
        public const int MaxNameLength = 12;
        /// <summary>이름 UTF-16 길이 상한(세이브 검증과 동일). 텍스트 요소 상한(MaxNameLength)과 함께 적용한다.</summary>
        public const int MaxNameUtf16 = 32;

        /// <summary>
        /// 표시를 속이거나 보이지 않는 문자: 방향 제어(U+202A–202E, U+2066–2069, U+061C), 제로폭·방향 표식(U+200B–200F, U+2060, U+FEFF),
        /// 폐기된 서식 문자(U+206A–206F), 소프트 하이픈(U+00AD), 줄/문단 구분자(U+2028/2029), 사설영역, 미할당 코드 포인트.
        /// 짝 없는 서로게이트는 호출자가 쌍 단위로 따로 검사한다.
        /// </summary>
        public static bool IsInvisibleOrBidi(char c)
        {
            if ((c >= '\u202A' && c <= '\u202E') || (c >= '\u2066' && c <= '\u2069')
                || (c >= '\u200B' && c <= '\u200F') || c == '\uFEFF'
                || c == '\u061C' || c == '\u2060' || (c >= '\u206A' && c <= '\u206F') || c == '\u00AD'
                || c == '\u2028' || c == '\u2029' || (c >= '\uE000' && c <= '\uF8FF')) return true;
            UnicodeCategory cat = char.GetUnicodeCategory(c);
            return cat == UnicodeCategory.Format || cat == UnicodeCategory.LineSeparator
                || cat == UnicodeCategory.ParagraphSeparator || cat == UnicodeCategory.PrivateUse
                || cat == UnicodeCategory.OtherNotAssigned;
        }

        public const int AppearanceCount = 6;

        public static readonly string[] GenderIds = { "male", "female", "nonbinary", "neutral" };
        public static readonly string[] GenderLabels = { "남성", "여성", "논바이너리", "지정 안 함" };

        public static readonly string[] SpeciesIds = { "human", "wolf", "fox", "tiger", "rabbit", "bear", "cat" };
        public static readonly string[] SpeciesLabels = { "인간", "늑대", "여우", "호랑이", "토끼", "곰", "고양이" };

        /// <summary>이름에 쓸 수 없는 문자(TMP 리치텍스트·템플릿 문법과 충돌).</summary>
        private const string ForbiddenChars = "<>{}\\";

        public const string ErrNameEmpty = "name_empty";
        public const string ErrNameTooLong = "name_too_long";
        public const string ErrNameChar = "name_invalid_char";
        public const string ErrGender = "gender_invalid";
        public const string ErrSpecies = "species_invalid";
        public const string ErrAppearance = "appearance_invalid";

        /// <summary>앞뒤 공백 제거 + 내부 연속 공백(탭 포함)을 한 칸으로. null 은 빈 문자열.</summary>
        public static string NormalizeName(string name)
        {
            if (name == null) return string.Empty;
            StringBuilder sb = new StringBuilder(name.Length);
            bool pendingSpace = false;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == ' ' || c == '\t' || c == '　')
                {
                    pendingSpace = sb.Length > 0;
                    continue;
                }
                if (pendingSpace)
                {
                    sb.Append(' ');
                    pendingSpace = false;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>이름 오류 코드(유효하면 빈 문자열). 입력은 정규화 전 원문이어도 된다.</summary>
        public static string CheckName(string name)
        {
            string n = NormalizeName(name);
            if (n.Length == 0) return ErrNameEmpty;
            if (n.Length > MaxNameUtf16) return ErrNameTooLong; // 조합 문자 폭탄 방지 + SaveValidator 와 같은 상한
            for (int i = 0; i < n.Length; i++)
            {
                char c = n[i];
                if (char.IsControl(c) || ForbiddenChars.IndexOf(c) >= 0 || IsInvisibleOrBidi(c)) return ErrNameChar;
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= n.Length || !char.IsLowSurrogate(n[i + 1])) return ErrNameChar; // 짝 없는 서로게이트
                    i++;
                }
                else if (char.IsLowSurrogate(c)) return ErrNameChar;
            }
            if (new StringInfo(n).LengthInTextElements > MaxNameLength) return ErrNameTooLong;
            return string.Empty;
        }

        public static bool IsValidGender(string id)
        {
            return Array.IndexOf(GenderIds, id) >= 0;
        }

        public static bool IsValidSpecies(string id)
        {
            return Array.IndexOf(SpeciesIds, id) >= 0;
        }

        public static bool IsValidAppearance(int preset)
        {
            return preset >= 1 && preset <= AppearanceCount;
        }

        public static string GenderLabel(string id)
        {
            int i = Array.IndexOf(GenderIds, id);
            return i >= 0 ? GenderLabels[i] : id;
        }

        public static string SpeciesLabel(string id)
        {
            int i = Array.IndexOf(SpeciesIds, id);
            return i >= 0 ? SpeciesLabels[i] : id;
        }

        public static ProfileValidation Validate(ProtagonistProfile profile)
        {
            if (profile == null) profile = new ProtagonistProfile();
            return Validate(profile.Name, profile.Gender, profile.Species, profile.Appearance);
        }

        public static ProfileValidation Validate(string name, string gender, string species, int appearance)
        {
            ProfileValidation v = new ProfileValidation();
            v.NameError = CheckName(name);
            v.GenderError = IsValidGender(gender) ? string.Empty : ErrGender;
            v.SpeciesError = IsValidSpecies(species) ? string.Empty : ErrSpecies;
            v.AppearanceError = IsValidAppearance(appearance) ? string.Empty : ErrAppearance;
            v.IsValid = v.NameError.Length == 0 && v.GenderError.Length == 0
                && v.SpeciesError.Length == 0 && v.AppearanceError.Length == 0;
            if (v.IsValid)
            {
                ProtagonistProfile p = new ProtagonistProfile();
                p.Name = NormalizeName(name);
                p.Gender = gender;
                p.Species = species;
                p.Appearance = appearance;
                v.Profile = p;
            }
            return v;
        }

        public static string MessageFor(string code)
        {
            switch (code)
            {
                case ErrNameEmpty: return "이름을 입력해 주세요.";
                case ErrNameTooLong: return "이름은 " + MaxNameLength + "자 이하로 입력해 주세요.";
                case ErrNameChar: return "이름에 사용할 수 없는 문자가 있습니다. (< > { } \\)";
                case ErrGender: return "성별을 선택해 주세요.";
                case ErrSpecies: return "종을 선택해 주세요.";
                case ErrAppearance: return "외형을 선택해 주세요.";
                default: return string.Empty;
            }
        }
    }
}
