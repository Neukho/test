using System;
using System.Collections.Generic;
using System.Globalization;
using MorpheHouse.Data;

namespace MorpheHouse.GameFlow
{
    /// <summary>텍스처 압축 형식(메모리 계산용).</summary>
    public enum TexFormat
    {
        Rgba32,
        Bc7,
        Astc4x4,
        Astc6x6,
        Astc8x8,
        Etc2Rgba8
    }

    /// <summary>아트 체크리스트의 분류.</summary>
    public enum ArtCategory
    {
        StandingBody,
        StandingFace,
        EventCg,
        CommonCg,
        Background,
        MenuIcon,
        /// <summary>앱 아이콘 등 인게임 텍스처가 아닌 행(메모리 계산에서 제외).</summary>
        Other
    }

    /// <summary>art_checklist.csv 의 한 행(텍스처 한 장).</summary>
    public sealed class ArtRow
    {
        public ArtCategory Category;
        public string File = string.Empty;
        public int Width;
        public int Height;
        /// <summary>우선순위가 "필수" 인가.</summary>
        public bool Required;
    }

    /// <summary>플랫폼별 텍스처 설정(형식·최대 크기·밉맵)과 예산.</summary>
    public sealed class PlatformProfile
    {
        public string Name = string.Empty;
        public TexFormat Standing = TexFormat.Bc7;
        public TexFormat Background = TexFormat.Bc7;
        public TexFormat Cg = TexFormat.Bc7;
        public TexFormat Icon = TexFormat.Bc7;
        /// <summary>긴 변의 최대 크기(Unity Max Size). 넘으면 비율을 유지하며 줄인다.</summary>
        public int MaxSizeStanding = 2048;
        public int MaxSizeLarge = 2048;
        public int MaxSizeIcon = 512;
        public bool Mipmaps;
        /// <summary>총 텍스처 메모리 예산(바이트). 01_TechDesign §8: PC 1 GB, 모바일 350 MB.</summary>
        public long BudgetBytes;
        /// <summary>UI 스프라이트 아틀라스 합계(바이트, 추정 고정값).</summary>
        public long UiAtlasBytes = 10L * 1024 * 1024;
        /// <summary>한 시점에 올려 둘 스탠딩 수(예산표 기준). 01_TechDesign §8: PC 최대 4, 모바일 2. VN 무대 자체는 2칸.</summary>
        public int MaxStandings = 2;

        /// <summary>ArtImportPostprocessor 와 같은 설정: PC=BC7, 밉맵 없음, 최대 2048(아이콘 512).</summary>
        public static PlatformProfile Pc()
        {
            PlatformProfile p = new PlatformProfile();
            p.Name = "PC";
            p.BudgetBytes = 1024L * 1024 * 1024;
            p.MaxStandings = 4;
            return p;
        }

        /// <summary>ArtImportPostprocessor 와 같은 설정: Android=ASTC 6x6(아이콘 4x4), 밉맵 없음, 최대 2048(아이콘 512).</summary>
        public static PlatformProfile Android()
        {
            PlatformProfile p = new PlatformProfile();
            p.Name = "Android";
            p.Standing = TexFormat.Astc6x6;
            p.Background = TexFormat.Astc6x6;
            p.Cg = TexFormat.Astc6x6;
            p.Icon = TexFormat.Astc4x4;
            p.BudgetBytes = 350L * 1024 * 1024;
            p.MaxStandings = 2;
            return p;
        }

        public TexFormat FormatOf(ArtCategory c)
        {
            switch (c)
            {
                case ArtCategory.StandingBody:
                case ArtCategory.StandingFace: return Standing;
                case ArtCategory.Background: return Background;
                case ArtCategory.EventCg:
                case ArtCategory.CommonCg: return Cg;
                default: return Icon;
            }
        }

        public int MaxSizeOf(ArtCategory c)
        {
            switch (c)
            {
                case ArtCategory.StandingBody:
                case ArtCategory.StandingFace: return MaxSizeStanding;
                case ArtCategory.MenuIcon: return MaxSizeIcon;
                default: return MaxSizeLarge;
            }
        }
    }

    /// <summary>한 장면(VN)의 동시 로드 구성과 추정 결과.</summary>
    public sealed class SceneEstimate
    {
        public int Standings;
        public int Backgrounds;
        public int Cgs;
        public long StandingBytes;
        public long BackgroundBytes;
        public long CgBytes;
        public long UiAtlasBytes;

        public long TotalBytes { get { return StandingBytes + BackgroundBytes + CgBytes + UiAtlasBytes; } }
    }

    /// <summary>
    /// 텍스처 메모리 추정기(순수): docs/art_checklist.csv 를 읽어 장면별 최대 동시 텍스처 메모리를 PC/Android 설정으로 계산한다.
    /// 형식별 용량은 블록 압축의 실제 규칙(블록 크기 올림 × 블록당 16바이트)으로 계산하므로 근사치가 아니라 GPU 메모리의 정확한 텍스처 크기에 가깝다
    /// (드라이버 정렬·밉맵 제외 옵션·스프라이트 아틀라스 여백은 모른다). 계산식은 docs/15_Performance_Guide.md 와 같다.
    /// </summary>
    public static class MemoryBudget
    {
        // 체크리스트에 규격이 글로 적힌 행의 기본 크기(ArtManifest 의 최종 일러스트 규격과 02_Art_Plan 기준).
        public const int StandingWidth = 1024;
        public const int StandingHeight = 2048;
        public const int LargeWidth = 1920;
        public const int LargeHeight = 1080;
        public const int IconSize = 512;

        /// <summary>형식의 블록 크기와 블록당 바이트. Rgba32 는 1×1 블록에 4바이트.</summary>
        public static void BlockInfo(TexFormat f, out int blockW, out int blockH, out int blockBytes)
        {
            blockBytes = 16;
            switch (f)
            {
                case TexFormat.Rgba32: blockW = 1; blockH = 1; blockBytes = 4; break;
                case TexFormat.Astc4x4: blockW = 4; blockH = 4; break;
                case TexFormat.Astc6x6: blockW = 6; blockH = 6; break;
                case TexFormat.Astc8x8: blockW = 8; blockH = 8; break;
                default: blockW = 4; blockH = 4; break; // Bc7, Etc2Rgba8
            }
        }

        /// <summary>텍스처 한 장의 바이트. mips 가 true 면 1×1 까지의 밉 체인을 모두 더한다.</summary>
        public static long TextureBytes(int width, int height, TexFormat format, bool mips)
        {
            if (width <= 0 || height <= 0) return 0L;
            int bw, bh, bb;
            BlockInfo(format, out bw, out bh, out bb);
            long total = 0;
            int w = width;
            int h = height;
            while (true)
            {
                long blocksX = (w + bw - 1) / bw;
                long blocksY = (h + bh - 1) / bh;
                total += blocksX * blocksY * bb;
                if (!mips || (w == 1 && h == 1)) break;
                w = Math.Max(1, w / 2);
                h = Math.Max(1, h / 2);
            }
            return total;
        }

        /// <summary>Unity 의 Max Size 규칙: 긴 변이 max 를 넘으면 비율을 유지해 줄인다(반올림, 최소 1).</summary>
        public static void ApplyMaxSize(int width, int height, int maxSize, out int outW, out int outH)
        {
            outW = width;
            outH = height;
            int longest = Math.Max(width, height);
            if (maxSize <= 0 || longest <= maxSize) return;
            double scale = (double)maxSize / longest;
            outW = Math.Max(1, (int)Math.Round(width * scale));
            outH = Math.Max(1, (int)Math.Round(height * scale));
        }

        /// <summary>행 하나가 플랫폼에서 차지하는 바이트(최대 크기·형식·밉맵 적용). 텍스처가 아닌 행은 0.</summary>
        public static long RowBytes(ArtRow row, PlatformProfile profile)
        {
            if (row == null || row.Category == ArtCategory.Other) return 0L;
            int w, h;
            ApplyMaxSize(row.Width, row.Height, profile.MaxSizeOf(row.Category), out w, out h);
            return TextureBytes(w, h, profile.FormatOf(row.Category), profile.Mipmaps);
        }

        // ---------------- 체크리스트 읽기 ----------------

        /// <summary>
        /// docs/art_checklist.csv 를 읽는다(열: 분류, 파일명, 규격, 우선순위, …). 알 수 없는 분류나 크기를 못 읽는 행은 errors 에 남기고,
        /// 크기를 못 읽는 행은 분류 기본 크기를 쓴다(스탠딩 얼굴은 "바디와 같은 캔버스" 라서 스탠딩 크기).
        /// </summary>
        public static List<ArtRow> ParseChecklist(string csv, List<string> errors)
        {
            List<ArtRow> rows = new List<ArtRow>();
            if (string.IsNullOrEmpty(csv))
            {
                if (errors != null) errors.Add("체크리스트가 비어 있습니다.");
                return rows;
            }
            CsvTable table = CsvTable.Parse(csv);
            for (int i = 0; i < table.Rows.Count; i++)
            {
                CsvRow r = table.Rows[i];
                int line = i + 2;
                string cat = r.Str("분류").Trim();
                string file = r.Str("파일명").Trim();
                if (cat.Length == 0 && file.Length == 0) continue;

                ArtCategory category;
                if (!TryCategory(cat, out category))
                {
                    if (errors != null) errors.Add("행 " + line + ": 알 수 없는 분류 '" + cat + "'");
                    continue;
                }
                if (category == ArtCategory.Other) continue;

                ArtRow row = new ArtRow();
                row.Category = category;
                row.File = file;
                row.Required = r.Str("우선순위").Trim() == "필수";
                int w, h;
                if (!TryDimensions(r.Str("규격"), out w, out h))
                {
                    DefaultSize(category, out w, out h);
                    bool inherits = category == ArtCategory.StandingFace; // "바디와 같은 캔버스" 는 오류가 아니다.
                    if (!inherits && errors != null) errors.Add("행 " + line + ": 규격 '" + r.Str("규격") + "' 에서 가로x세로를 읽지 못해 기본 크기 사용");
                }
                row.Width = w;
                row.Height = h;
                rows.Add(row);
            }
            return rows;
        }

        public static bool TryCategory(string text, out ArtCategory category)
        {
            category = ArtCategory.Other;
            switch ((text ?? string.Empty).Trim())
            {
                case "스탠딩 바디": category = ArtCategory.StandingBody; return true;
                case "스탠딩 얼굴": category = ArtCategory.StandingFace; return true;
                case "이벤트 CG": category = ArtCategory.EventCg; return true;
                case "공통 CG": category = ArtCategory.CommonCg; return true;
                case "배경": category = ArtCategory.Background; return true;
                case "메뉴 아이콘": category = ArtCategory.MenuIcon; return true;
                case "UI": category = ArtCategory.Other; return true;
                default: return false;
            }
        }

        public static void DefaultSize(ArtCategory c, out int w, out int h)
        {
            switch (c)
            {
                case ArtCategory.StandingBody:
                case ArtCategory.StandingFace: w = StandingWidth; h = StandingHeight; break;
                case ArtCategory.MenuIcon: w = IconSize; h = IconSize; break;
                default: w = LargeWidth; h = LargeHeight; break;
            }
        }

        /// <summary>"1920x1080", "1024x2048 이상(세로 2048+)", "512x512(투명)" 처럼 앞쪽의 가로x세로를 읽는다.</summary>
        public static bool TryDimensions(string text, out int w, out int h)
        {
            w = 0;
            h = 0;
            if (string.IsNullOrEmpty(text)) return false;
            int x = text.IndexOf('x');
            if (x <= 0) x = text.IndexOf('X');
            if (x <= 0) return false;
            int a = x - 1;
            while (a >= 0 && text[a] >= '0' && text[a] <= '9') a--;
            int b = x + 1;
            while (b < text.Length && text[b] >= '0' && text[b] <= '9') b++;
            string ws = text.Substring(a + 1, x - a - 1);
            string hs = text.Substring(x + 1, b - x - 1);
            if (!int.TryParse(ws, NumberStyles.Integer, CultureInfo.InvariantCulture, out w)) return false;
            if (!int.TryParse(hs, NumberStyles.Integer, CultureInfo.InvariantCulture, out h)) return false;
            return w > 0 && h > 0;
        }

        // ---------------- 추정 ----------------

        /// <summary>한 분류에서 가장 큰 텍스처의 바이트(없으면 0). 같은 규격이면 모두 같은 값이다.</summary>
        public static long LargestBytes(IList<ArtRow> rows, ArtCategory category, PlatformProfile profile)
        {
            long best = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Category != category) continue;
                long b = RowBytes(rows[i], profile);
                if (b > best) best = b;
            }
            return best;
        }

        /// <summary>
        /// VN 한 장면의 최악 동시 메모리: 스탠딩 standings 명(각 몸 + 얼굴 레이어) + 배경 backgrounds 장 + CG cgs 장 + UI 아틀라스.
        /// 배경 전환 중에는 이전·다음 배경이 함께 올라가므로 기본 2장, CG 가 떠도 스탠딩 레이어는 숨길 뿐 메모리에 남아 있다.
        /// </summary>
        public static SceneEstimate Scene(IList<ArtRow> rows, PlatformProfile profile, int standings, int backgrounds, int cgs)
        {
            SceneEstimate e = new SceneEstimate();
            e.Standings = standings;
            e.Backgrounds = backgrounds;
            e.Cgs = cgs;
            long perStanding = LargestBytes(rows, ArtCategory.StandingBody, profile) + LargestBytes(rows, ArtCategory.StandingFace, profile);
            long cg = Math.Max(LargestBytes(rows, ArtCategory.EventCg, profile), LargestBytes(rows, ArtCategory.CommonCg, profile));
            e.StandingBytes = perStanding * standings;
            e.BackgroundBytes = LargestBytes(rows, ArtCategory.Background, profile) * backgrounds;
            e.CgBytes = cg * cgs;
            e.UiAtlasBytes = profile.UiAtlasBytes;
            return e;
        }

        /// <summary>표준 최악 장면: 스탠딩 2명 + 배경 2장(전환 중) + CG 1장.</summary>
        public static SceneEstimate PeakScene(IList<ArtRow> rows, PlatformProfile profile)
        {
            return Scene(rows, profile, 2, 2, 1);
        }

        /// <summary>
        /// 모든 텍스처를 한 번씩 올렸을 때의 합(예전 ArtLoader 는 해제하지 않아 여기에 수렴했다. 지금은 LRU 상한이 걸린다: SteadyStateCacheBytes).
        /// requiredOnly 면 "필수" 행만. UI 아틀라스는 더하지 않는다.
        /// </summary>
        public static long CacheGrowthBytes(IList<ArtRow> rows, PlatformProfile profile, bool requiredOnly)
        {
            long total = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                if (requiredOnly && !rows[i].Required) continue;
                total += RowBytes(rows[i], profile);
            }
            return total;
        }

        /// <summary>
        /// LRU 캐시(ArtLoader)의 정상 상태 상한 추정: 분류별 "가장 큰 텍스처 × 캐시 개수"의 합(최악). 개수 상한이 걸리므로 플레이 시간과 무관하게 이 값 이하로 수렴한다.
        /// 스탠딩은 몸·얼굴 중 큰 쪽, CG 는 이벤트·공통 중 큰 쪽. UI 아틀라스는 더하지 않는다.
        /// </summary>
        public static long SteadyStateCacheBytes(IList<ArtRow> rows, PlatformProfile profile, int standingPieces, int backgrounds, int cgs, int icons)
        {
            long standing = Math.Max(LargestBytes(rows, ArtCategory.StandingBody, profile), LargestBytes(rows, ArtCategory.StandingFace, profile));
            long cg = Math.Max(LargestBytes(rows, ArtCategory.EventCg, profile), LargestBytes(rows, ArtCategory.CommonCg, profile));
            return standing * standingPieces
                + LargestBytes(rows, ArtCategory.Background, profile) * backgrounds
                + cg * cgs
                + LargestBytes(rows, ArtCategory.MenuIcon, profile) * icons;
        }

        /// <summary>분류별 합계(전체 행, 해제 없음).</summary>
        public static long CategoryBytes(IList<ArtRow> rows, ArtCategory category, PlatformProfile profile, bool requiredOnly)
        {
            long total = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Category != category) continue;
                if (requiredOnly && !rows[i].Required) continue;
                total += RowBytes(rows[i], profile);
            }
            return total;
        }

        public static int CountOf(IList<ArtRow> rows, ArtCategory category, bool requiredOnly)
        {
            int n = 0;
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].Category == category && (!requiredOnly || rows[i].Required)) n++;
            return n;
        }

        /// <summary>추정 합계가 예산 안인가(headroom 은 남겨 둘 비율, 예 0.25 = 예산의 75% 까지만 허용).</summary>
        public static bool FitsBudget(long bytes, PlatformProfile profile, double headroom)
        {
            double limit = profile.BudgetBytes * (1.0 - Math.Max(0.0, Math.Min(0.9, headroom)));
            return bytes <= limit;
        }

        public static double ToMb(long bytes)
        {
            return bytes / (1024.0 * 1024.0);
        }
    }
}
