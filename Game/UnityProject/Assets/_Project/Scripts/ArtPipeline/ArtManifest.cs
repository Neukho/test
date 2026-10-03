using System.Collections.Generic;

namespace MorpheHouse.ArtPipeline
{
    public enum ArtKind
    {
        StandingBody,
        StandingFace,
        EventCg,
        Background,
        MenuIcon,
        /// <summary>인물에 속하지 않는 공통 CG(HAREM_E1, COMMON_CE03 ...). 경로 Art/CG/Common/CG_&lt;키&gt;.</summary>
        CommonCg
    }

    /// <summary>에셋 한 장의 명세. ResPath 는 Resources 기준 경로(확장자 없음)이며 최종 일러스트의 파일명과 같다.</summary>
    public sealed class ArtSpec
    {
        public string ResPath;
        public ArtKind Kind;
        public string CharId = string.Empty;
        public string Variant = string.Empty;
        public int Width;
        public int Height;
    }

    /// <summary>파일명 규칙의 단일 출처. 02_Art_Plan.md §6 와 동일하게 유지한다.</summary>
    public static class ArtKeys
    {
        public static readonly string[] Outfits = { "Casual", "Uniform", "Date" };
        public static readonly string[] Expressions = { "Neutral", "Smile", "Shy", "Angry", "Sad", "Surprised" };
        public static readonly string[] Times = { "Day", "Evening", "Night" };

        public static readonly string[] Places =
        {
            "Cafe_Interior", "Cafe_Exterior", "Street",
            "Lighthouse", "FishMarket", "NightMarket", "Dojo", "LiveClub", "Greenhouse", "BookStreet", "Beach", "Rooftop"
        };

        /// <summary>메뉴 40종 ID. 앞 12개가 초기 메뉴.</summary>
        public static readonly string[] MenuIds =
        {
            "HouseCoffee", "Latte", "CinnamonLatte", "Espresso", "MilkTea", "HerbTea",
            "SmokedCheeseBoard", "GrilledFish", "SteakSandwich", "StrawberryTart", "MacaronSet", "SeasonalParfait",
            "TomatoStew", "SeafoodSoup", "BreadBasket", "HoneyToast", "SpicyNoodle", "FishBurger", "CitrusCocktail", "WhiskyFloat",
            "HarborPie", "LemonCake", "ChocolateFondant", "PotatoGratin", "CreamPasta", "ClamChowder", "RoastChicken", "MushroomRisotto",
            "ApplePie", "PancakeStack", "IceCreamFloat", "GingerTea", "HotChocolate", "BerryScone", "CheeseCake", "SalmonBowl",
            "MoonCookie", "NightCaramel", "StarSoda", "MorenaSpecial"
        };

        public static string CharName(string charId)
        {
            return char.ToUpperInvariant(charId[0]) + charId.Substring(1);
        }

        public static string StandingBody(string charId, string outfit)
        {
            string n = CharName(charId);
            return "Art/Standing/" + n + "/Std_" + n + "_" + outfit + "_Body";
        }

        public static string StandingFace(string charId, string outfit, string expression)
        {
            string n = CharName(charId);
            return "Art/Standing/" + n + "/Std_" + n + "_" + outfit + "_Face_" + expression;
        }

        /// <summary>번호 1~5 는 개인 이벤트 CG, 6 은 트루 엔딩 추가 CG.</summary>
        public static string Cg(string charId, int number)
        {
            string n = CharName(charId);
            return "Art/CG/" + n + "/CG_" + n + "_E" + number;
        }

        /// <summary>공통 CG 경로: Art/CG/Common/CG_&lt;KEY&gt;(예 CG_HAREM_E1.png). key 는 대문자 정규화된 키.</summary>
        public static string CommonCg(string key)
        {
            return "Art/CG/Common/CG_" + key;
        }

        public static string Background(string place, string time)
        {
            return "Art/BG/BG_" + place + "_" + time;
        }

        public static string MenuIcon(string menuId)
        {
            return "Art/Icons/Menu/ICON_Menu_" + menuId;
        }
    }

    /// <summary>플레이스홀더로 생성하는 전체 에셋 목록과 개별 명세 팩토리.</summary>
    public static class ArtManifest
    {
        public const int StandingWidth = 512;
        public const int StandingHeight = 1024;
        public const int CgWidth = 960;
        public const int CgHeight = 540;
        public const int IconSize = 128;

        public static ArtSpec StandingBody(string charId, string outfit)
        {
            return new ArtSpec { ResPath = ArtKeys.StandingBody(charId, outfit), Kind = ArtKind.StandingBody, CharId = charId, Variant = outfit, Width = StandingWidth, Height = StandingHeight };
        }

        public static ArtSpec StandingFace(string charId, string outfit, string expression)
        {
            return new ArtSpec { ResPath = ArtKeys.StandingFace(charId, outfit, expression), Kind = ArtKind.StandingFace, CharId = charId, Variant = expression, Width = StandingWidth, Height = StandingHeight };
        }

        public static ArtSpec Cg(string charId, int number)
        {
            return new ArtSpec { ResPath = ArtKeys.Cg(charId, number), Kind = ArtKind.EventCg, CharId = charId, Variant = number.ToString(), Width = CgWidth, Height = CgHeight };
        }

        /// <summary>공통 CG 명세(인물 없음, 플레이스홀더는 키 해시 색).</summary>
        public static ArtSpec CommonCg(string key)
        {
            return new ArtSpec { ResPath = ArtKeys.CommonCg(key), Kind = ArtKind.CommonCg, Variant = key, Width = CgWidth, Height = CgHeight };
        }

        /// <summary>플레이스홀더를 미리 만들어 두는 공통 CG 키(그 밖의 키는 런타임 폴백으로 그려진다).</summary>
        public static readonly string[] CommonCgKeys = { "HAREM_E1" };

        public static ArtSpec Background(string place, string time)
        {
            return new ArtSpec { ResPath = ArtKeys.Background(place, time), Kind = ArtKind.Background, Variant = place + "|" + time, Width = CgWidth, Height = CgHeight };
        }

        public static ArtSpec MenuIcon(string menuId)
        {
            return new ArtSpec { ResPath = ArtKeys.MenuIcon(menuId), Kind = ArtKind.MenuIcon, Variant = menuId, Width = IconSize, Height = IconSize };
        }

        /// <summary>모든 에셋 명세(스탠딩 몸 18 + 얼굴 108 + CG 36 + 공통 CG 1 + 배경 36 + 아이콘 40 = 239).</summary>
        public static List<ArtSpec> All(string[] charIds)
        {
            List<ArtSpec> list = new List<ArtSpec>();
            foreach (string c in charIds)
            {
                foreach (string outfit in ArtKeys.Outfits)
                {
                    list.Add(StandingBody(c, outfit));
                    foreach (string expr in ArtKeys.Expressions) list.Add(StandingFace(c, outfit, expr));
                }
                for (int n = 1; n <= 6; n++) list.Add(Cg(c, n));
            }
            foreach (string key in CommonCgKeys) list.Add(CommonCg(key));
            foreach (string place in ArtKeys.Places)
                foreach (string time in ArtKeys.Times) list.Add(Background(place, time));
            foreach (string id in ArtKeys.MenuIds) list.Add(MenuIcon(id));
            return list;
        }
    }
}
