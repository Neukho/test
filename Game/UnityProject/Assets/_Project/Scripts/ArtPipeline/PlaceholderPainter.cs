using System;
using UnityEngine;

namespace MorpheHouse.ArtPipeline
{
    /// <summary>
    /// 단색 실루엣 플레이스홀더를 픽셀 배열로 그린다(외부 이미지·폰트 불필요).
    /// 에디터 생성기(PNG 저장)와 런타임 폴백(스프라이트 즉석 생성)이 같은 그림을 쓴다.
    /// 좌표 원점은 좌하단(Texture2D 규칙).
    /// </summary>
    public static class PlaceholderPainter
    {
        private static readonly Color32 Clear = new Color32(0, 0, 0, 0);
        private static readonly Color32 Ink = new Color32(35, 30, 40, 255);

        /// <summary>캐릭터 대표색(털 색 기반 실루엣 색).</summary>
        public static Color32 CharColor(string charId)
        {
            switch (charId)
            {
                case "howl": return new Color32(128, 120, 116, 255);
                case "johan": return new Color32(196, 206, 224, 255);
                case "beomjun": return new Color32(224, 140, 56, 255);
                case "haru": return new Color32(244, 232, 238, 255);
                case "gomseok": return new Color32(110, 76, 56, 255);
                case "nabi": return new Color32(52, 54, 64, 255);
                default: return new Color32(150, 150, 150, 255);
            }
        }

        public static Color32[] Paint(ArtSpec spec)
        {
            Color32[] px = new Color32[spec.Width * spec.Height];
            switch (spec.Kind)
            {
                case ArtKind.StandingBody: PaintBody(px, spec); break;
                case ArtKind.StandingFace: PaintFace(px, spec); break;
                case ArtKind.EventCg: PaintCg(px, spec); break;
                case ArtKind.CommonCg: PaintCommonCg(px, spec); break;
                case ArtKind.Background: PaintBackground(px, spec); break;
                default: PaintIcon(px, spec); break;
            }
            return px;
        }

        // ---------------- 스탠딩 ----------------

        private static void PaintBody(Color32[] px, ArtSpec s)
        {
            int w = s.Width, h = s.Height;
            Color32 fur = CharColor(s.CharId);
            Color32 cloth = Shade(fur, s.Variant == "Uniform" ? 1.25f : (s.Variant == "Date" ? 0.55f : 0.85f));

            Fill(px, Clear);
            // 귀(머리 뒤)
            Ellipse(px, w, h, w / 2 - 78, 900, 34, 58, fur);
            Ellipse(px, w, h, w / 2 + 78, 900, 34, 58, fur);
            // 몸통·팔·다리
            Ellipse(px, w, h, w / 2, 520, 150, 330, cloth);
            Ellipse(px, w, h, w / 2 - 170, 560, 42, 190, fur);
            Ellipse(px, w, h, w / 2 + 170, 560, 42, 190, fur);
            Rect(px, w, h, w / 2 - 95, 20, 80, 260, Shade(cloth, 0.8f));
            Rect(px, w, h, w / 2 + 15, 20, 80, 260, Shade(cloth, 0.8f));
            // 머리(얼굴 레이어가 올라갈 자리)
            Circle(px, w, h, w / 2, 780, 125, fur);
        }

        private static void PaintFace(Color32[] px, ArtSpec s)
        {
            int w = s.Width, h = s.Height;
            int cx = w / 2, cy = 780;
            Fill(px, Clear);

            Color32 blush = new Color32(240, 120, 130, 200);
            if (s.Variant == "Shy")
            {
                Circle(px, w, h, cx - 62, cy - 14, 20, blush);
                Circle(px, w, h, cx + 62, cy - 14, 20, blush);
            }

            // 눈
            int eyeR = s.Variant == "Surprised" ? 20 : 13;
            Circle(px, w, h, cx - 44, cy + 14, eyeR, Ink);
            Circle(px, w, h, cx + 44, cy + 14, eyeR, Ink);

            // 눈썹
            if (s.Variant == "Angry")
            {
                Line(px, w, h, cx - 70, cy + 52, cx - 22, cy + 36, 8, Ink);
                Line(px, w, h, cx + 70, cy + 52, cx + 22, cy + 36, 8, Ink);
            }
            else if (s.Variant == "Sad")
            {
                Line(px, w, h, cx - 70, cy + 36, cx - 22, cy + 52, 8, Ink);
                Line(px, w, h, cx + 70, cy + 36, cx + 22, cy + 52, 8, Ink);
            }

            // 입
            switch (s.Variant)
            {
                case "Smile": Arc(px, w, h, cx, cy - 30, 42, 24, 200, 340, 7, Ink); break;
                case "Shy": Arc(px, w, h, cx, cy - 44, 20, 8, 200, 340, 6, Ink); break;
                case "Angry": Line(px, w, h, cx - 30, cy - 52, cx + 30, cy - 52, 7, Ink); break;
                case "Sad": Arc(px, w, h, cx, cy - 66, 36, 18, 20, 160, 7, Ink); break;
                case "Surprised": Circle(px, w, h, cx, cy - 50, 16, Ink); break;
                default: Line(px, w, h, cx - 24, cy - 48, cx + 24, cy - 48, 6, Ink); break;
            }
        }

        // ---------------- CG / 배경 / 아이콘 ----------------

        private static void PaintCg(Color32[] px, ArtSpec s)
        {
            int w = s.Width, h = s.Height;
            Color32 c = CharColor(s.CharId);
            Gradient(px, w, h, new Color32(40, 46, 68, 255), Shade(c, 0.45f));
            Circle(px, w, h, w / 2 - 130, h / 2 - 20, 120, c);
            Ellipse(px, w, h, w / 2 - 130, h / 2 - 190, 150, 150, c);
            Circle(px, w, h, w / 2 + 160, h / 2 - 10, 100, new Color32(210, 170, 90, 255));
            Ellipse(px, w, h, w / 2 + 160, h / 2 - 170, 130, 130, new Color32(210, 170, 90, 255));
            int n;
            int.TryParse(s.Variant, out n);
            for (int i = 0; i < n; i++) Rect(px, w, h, 24 + i * 26, h - 44, 18, 20, new Color32(255, 255, 255, 220));
        }

        /// <summary>공통 CG: 중립 그라데이션 + 키 해시 색의 큰 원 두 개(어느 키인지 눈으로 구분).</summary>
        private static void PaintCommonCg(Color32[] px, ArtSpec s)
        {
            int w = s.Width, h = s.Height;
            int seed = Hash(s.Variant);
            Color32 c = new Color32((byte)(70 + (seed & 0x7F)), (byte)(70 + ((seed >> 7) & 0x7F)), (byte)(70 + ((seed >> 14) & 0x7F)), 255);
            Gradient(px, w, h, new Color32(44, 48, 62, 255), Shade(c, 0.5f));
            Circle(px, w, h, w / 2 - 110, h / 2, 130, c);
            Circle(px, w, h, w / 2 + 110, h / 2, 130, Shade(c, 1.2f));
        }

        private static void PaintBackground(Color32[] px, ArtSpec s)
        {
            int w = s.Width, h = s.Height;
            string[] parts = s.Variant.Split('|');
            string place = parts[0];
            string time = parts.Length > 1 ? parts[1] : "Day";

            Color32 top, bottom;
            if (time == "Night") { top = new Color32(14, 22, 48, 255); bottom = new Color32(52, 62, 96, 255); }
            else if (time == "Evening") { top = new Color32(236, 146, 92, 255); bottom = new Color32(96, 64, 118, 255); }
            else { top = new Color32(132, 190, 236, 255); bottom = new Color32(250, 236, 204, 255); }
            Gradient(px, w, h, top, bottom);

            int seed = Hash(place);
            Color32 tint = new Color32((byte)(80 + (seed & 0x7F)), (byte)(80 + ((seed >> 7) & 0x7F)), (byte)(80 + ((seed >> 14) & 0x7F)), 255);
            if (time == "Night") tint = Shade(tint, 0.45f);
            else if (time == "Evening") tint = Shade(tint, 0.7f);

            // 건물 실루엣
            int x = 0, i = 0;
            while (x < w)
            {
                int bw = 70 + ((seed >> (i % 16)) & 0x3F);
                int bh = 120 + ((seed * (i + 3)) & 0xFF);
                Rect(px, w, h, x, h / 4, bw - 6, bh, Shade(tint, 0.8f + 0.05f * (i % 4)));
                if (time != "Day") Rect(px, w, h, x + 14, h / 4 + bh - 50, 14, 18, new Color32(255, 220, 130, 255));
                x += bw;
                i++;
            }
            // 지면
            Rect(px, w, h, 0, 0, w, h / 4, Shade(tint, 0.55f));
        }

        private static void PaintIcon(Color32[] px, ArtSpec s)
        {
            int w = s.Width, h = s.Height;
            int seed = Hash(s.Variant);
            Color32 food = new Color32((byte)(150 + (seed & 0x6F)), (byte)(100 + ((seed >> 8) & 0x6F)), (byte)(60 + ((seed >> 16) & 0x5F)), 255);
            Fill(px, Clear);
            Circle(px, w, h, w / 2, h / 2, w / 2 - 6, new Color32(245, 242, 235, 255));
            Circle(px, w, h, w / 2, h / 2, w / 2 - 20, food);
            Circle(px, w, h, w / 2 - 14, h / 2 + 14, w / 8, Shade(food, 1.25f));
        }

        // ---------------- 그리기 도우미 ----------------

        private static int Hash(string s)
        {
            unchecked
            {
                int hash = (int)2166136261;
                for (int i = 0; i < s.Length; i++) hash = (hash ^ s[i]) * 16777619;
                return hash & 0x7FFFFFFF;
            }
        }

        private static Color32 Shade(Color32 c, float f)
        {
            return new Color32(ClampByte(c.r * f), ClampByte(c.g * f), ClampByte(c.b * f), c.a);
        }

        private static byte ClampByte(float v)
        {
            return (byte)Math.Max(0, Math.Min(255, (int)v));
        }

        private static void Fill(Color32[] px, Color32 c)
        {
            for (int i = 0; i < px.Length; i++) px[i] = c;
        }

        private static void Gradient(Color32[] px, int w, int h, Color32 top, Color32 bottom)
        {
            for (int y = 0; y < h; y++)
            {
                float t = y / (float)(h - 1); // y=0 이 아래
                Color32 row = new Color32(
                    ClampByte(bottom.r + (top.r - bottom.r) * t),
                    ClampByte(bottom.g + (top.g - bottom.g) * t),
                    ClampByte(bottom.b + (top.b - bottom.b) * t), 255);
                for (int x = 0; x < w; x++) px[y * w + x] = row;
            }
        }

        private static void Set(Color32[] px, int w, int h, int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            px[y * w + x] = c;
        }

        private static void Rect(Color32[] px, int w, int h, int x0, int y0, int rw, int rh, Color32 c)
        {
            for (int y = y0; y < y0 + rh; y++)
                for (int x = x0; x < x0 + rw; x++) Set(px, w, h, x, y, c);
        }

        private static void Circle(Color32[] px, int w, int h, int cx, int cy, int r, Color32 c)
        {
            Ellipse(px, w, h, cx, cy, r, r, c);
        }

        private static void Ellipse(Color32[] px, int w, int h, int cx, int cy, int rx, int ry, Color32 c)
        {
            for (int y = -ry; y <= ry; y++)
            {
                for (int x = -rx; x <= rx; x++)
                {
                    float nx = x / (float)rx, ny = y / (float)ry;
                    if (nx * nx + ny * ny <= 1f) Set(px, w, h, cx + x, cy + y, c);
                }
            }
        }

        private static void Line(Color32[] px, int w, int h, int x0, int y0, int x1, int y1, int thick, Color32 c)
        {
            int steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
            for (int i = 0; i <= steps; i++)
            {
                float t = steps == 0 ? 0f : i / (float)steps;
                Circle(px, w, h, (int)(x0 + (x1 - x0) * t), (int)(y0 + (y1 - y0) * t), thick / 2, c);
            }
        }

        private static void Arc(Color32[] px, int w, int h, int cx, int cy, int rx, int ry, int startDeg, int endDeg, int thick, Color32 c)
        {
            for (int d = startDeg; d <= endDeg; d += 2)
            {
                double a = d * Math.PI / 180.0;
                Circle(px, w, h, cx + (int)(Math.Cos(a) * rx), cy + (int)(Math.Sin(a) * ry), thick / 2, c);
            }
        }
    }
}
