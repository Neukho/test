using System;
using System.Collections.Generic;
using System.Text;
using MorpheHouse.Data;
using MorpheHouse.Dialogue;

namespace MorpheHouse.GameFlow
{
    /// <summary>주인공 생성 화면 미리보기 한 줄: 한 인물이 주인공을 부르는 말(처음 / 가까워진 뒤).</summary>
    public sealed class PreviewEntry
    {
        public string CharId = string.Empty;
        public string CharName = string.Empty;
        public string FirstCall = string.Empty;
        public string CloseCall = string.Empty;
        public string PlayerCallsFirst = string.Empty;
        public string PlayerCallsClose = string.Empty;
    }

    /// <summary>
    /// "이 이름·성별이면 NPC 들이 나를 어떻게 부를까"를 호칭표(AddressBook)와 PronounResolver 로 계산한다.
    /// 실제 대사와 같은 코드를 쓰므로 미리보기와 게임 속 호칭이 어긋나지 않는다.
    /// </summary>
    public static class ProfilePreview
    {
        public static List<PreviewEntry> Build(ProtagonistProfile profile, AddressBook book, string locale, Func<string, string> nameOf)
        {
            ProtagonistProfile p = profile ?? ProtagonistProfile.Default();
            string name = ProfileValidator.NormalizeName(p.Name);
            GameState temp = new GameState();
            temp.PlayerName = name.Length == 0 ? ProtagonistProfile.DefaultName : name;
            temp.Gender = p.Gender;
            for (int i = 0; i < CharIds.All.Length; i++) temp.Chars[CharIds.All[i]] = new CharState();
            PronounResolver resolver = new PronounResolver(temp, locale, book);

            List<PreviewEntry> list = new List<PreviewEntry>();
            for (int i = 0; i < CharIds.All.Length; i++)
            {
                string id = CharIds.All[i];
                PreviewEntry e = new PreviewEntry();
                e.CharId = id;
                e.CharName = nameOf == null ? id : (nameOf(id) ?? id);
                temp.Chars[id].Aff = 0f;
                e.FirstCall = resolver.CallsPlayer(id);
                e.PlayerCallsFirst = resolver.PlayerCalls(id);
                temp.Chars[id].Aff = AddressBook.CloseMin;
                e.CloseCall = resolver.CallsPlayer(id);
                e.PlayerCallsClose = resolver.PlayerCalls(id);
                temp.Chars[id].Aff = 0f;
                list.Add(e);
            }
            return list;
        }

        /// <summary>미리보기 텍스트(여러 줄). 예: `하울: "민준 씨…"  (가까워지면 "민준…")`.</summary>
        public static string ToText(List<PreviewEntry> entries)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                PreviewEntry e = entries[i];
                if (i > 0) sb.Append('\n');
                sb.Append(e.CharName).Append(": \"").Append(e.FirstCall).Append("…\"");
                if (e.CloseCall != e.FirstCall) sb.Append(Loc.F("preview_close_call", "  (가까워지면 \"{0}…\")", e.CloseCall));
            }
            return sb.ToString();
        }
    }
}
