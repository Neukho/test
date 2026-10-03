using System;
using System.Collections.Generic;

namespace MorpheHouse.GameFlow
{
    /// <summary>축 정렬 사각형(왼쪽 아래 원점, 위쪽이 +y). 캔버스 기준 해상도 단위.</summary>
    public struct LayoutRect
    {
        public float X;
        public float Y;
        public float W;
        public float H;

        public LayoutRect(float x, float y, float w, float h)
        {
            X = x;
            Y = y;
            W = w;
            H = h;
        }

        public float Right { get { return X + W; } }

        public float Top { get { return Y + H; } }

        /// <summary>other 가 이 사각형 안에 (오차 eps 안에서) 들어가는가.</summary>
        public bool Contains(LayoutRect other, float eps)
        {
            return other.X >= X - eps && other.Y >= Y - eps && other.Right <= Right + eps && other.Top <= Top + eps;
        }

        /// <summary>두 사각형이 eps 보다 크게 겹치는가(맞닿기만 하면 false).</summary>
        public bool Overlaps(LayoutRect other, float eps)
        {
            return X < other.Right - eps && other.X < Right - eps && Y < other.Top - eps && other.Y < Top - eps;
        }

        public override string ToString()
        {
            return "(" + X.ToString("0.#") + "," + Y.ToString("0.#") + " " + W.ToString("0.#") + "x" + H.ToString("0.#") + ")";
        }
    }

    /// <summary>
    /// 코드에 적힌 패널 하나의 배치 수치(앵커·피벗·크기·위치). 세이프 에어리어 루트(또는 부모 패널) 기준.
    /// StretchX 면 가로가 부모를 가득 채우고 W 는 sizeDelta.x(보통 음수 여백)를 뜻한다.
    /// SourceFile/SourceSnippet 은 이 수치가 적힌 코드 위치로, 하네스가 코드에 그 문자열이 그대로 있는지 확인해 표와 코드의 어긋남을 잡는다.
    /// </summary>
    public sealed class PanelSpec
    {
        public string Screen;
        public string Name;
        public string Parent;
        public float AnchorX;
        public float AnchorY;
        public float PivotX;
        public float PivotY;
        public float W;
        public float H;
        public float PosX;
        public float PosY;
        public bool StretchX;
        /// <summary>
        /// 이 패널이 보이는 상태 태그(쉼표로 구분). "*" 는 항상 보임. 겹침 검사는 같은 화면에서 태그를 하나라도 공유하는(또는 한쪽이 "*" 인) 패널 쌍만 한다.
        /// "-" 는 겹침 검사에서 제외(일부러 겹치는 것).
        /// </summary>
        public string States = "*";

        /// <summary>두 패널이 동시에 보일 수 있는가.</summary>
        public bool CoVisible(PanelSpec other)
        {
            if (States == "-" || other.States == "-") return false;
            if (States == "*" || other.States == "*") return true;
            string[] a = States.Split(',');
            string[] b = other.States.Split(',');
            for (int i = 0; i < a.Length; i++)
                for (int j = 0; j < b.Length; j++)
                    if (a[i].Trim() == b[j].Trim()) return true;
            return false;
        }
        public string SourceFile = string.Empty;
        public string SourceSnippet = string.Empty;
    }

    /// <summary>세로 레이아웃 그룹의 자식 높이 합 검사용 명세(패딩·간격·자식 높이들).</summary>
    public sealed class StackSpec
    {
        public string Name;
        /// <summary>대상 패널 이름(<see cref="PanelSpec.Name"/>).</summary>
        public string Panel;
        public float Padding;
        public float Spacing;
        public List<float> ChildHeights = new List<float>();
        /// <summary>자식마다 'b'(버튼, 모바일에서 최소 터치 높이까지 커짐) 또는 'x'(그 밖). 비어 있으면 전부 'x'.</summary>
        public string ButtonMask = string.Empty;
        /// <summary>true 면 내용이 패널보다 길 때 스크롤로 처리한다(UiKit.MakeScrollColumn). 이때는 고정 머리(버튼이 아닌 자식) + 버튼 <see cref="MinVisibleButtons"/>개가 보여야 한다.</summary>
        public bool Scrollable;
        public int MinVisibleButtons = 2;
        /// <summary>모바일에서 자식 높이가 달라지는 스택(예: 갤러리 본문을 줄임). 비어 있으면 같음. 터치 검사(minButton > 0)에서만 쓴다.</summary>
        public List<float> MobileChildHeights = new List<float>();
        /// <summary>모바일 패널 높이를 버튼 최소 높이로 계산(루트 목록처럼 minButton 에 따라 달라질 때). null 이면 MobileH/패널 높이.</summary>
        public Func<float, float> MobileHeightOf;
        /// <summary>모바일에서 패널을 이 높이로 키운다(0 = 그대로). 코드의 모바일 분기 수치와 같아야 한다.</summary>
        public float MobileH;
        /// <summary>true 면 모바일에서 패널이 내용 높이에 맞춰 자란다(상위 패널에 들어가는지는 별도 검사: RouteActionsFit).</summary>
        public bool GrowsOnMobile;

        public float ContentHeight()
        {
            return ContentHeight(0f);
        }

        /// <summary>버튼 자식을 minButton 이상으로 키웠을 때의 내용 높이.</summary>
        public float ContentHeight(float minButton)
        {
            float sum = Padding * 2f;
            for (int i = 0; i < ChildHeights.Count; i++) sum += ChildHeight(i, minButton);
            if (ChildHeights.Count > 1) sum += Spacing * (ChildHeights.Count - 1);
            return sum;
        }

        public float ChildHeight(int i, float minButton)
        {
            float h = (minButton > 0f && MobileChildHeights.Count == ChildHeights.Count) ? MobileChildHeights[i] : ChildHeights[i];
            bool isButton = i < ButtonMask.Length && ButtonMask[i] == 'b';
            return isButton && h < minButton ? minButton : h;
        }

        /// <summary>스크롤 처리일 때 스크롤 맨 위에서 보여야 하는 높이: 패딩 + 위에서부터 버튼 MinVisibleButtons 개째까지의 자식(나머지는 스크롤로 닿는다).</summary>
        public float MinVisibleHeight(float minButton)
        {
            float sum = Padding * 2f;
            int shown = 0;
            int children = 0;
            for (int i = 0; i < ChildHeights.Count; i++)
            {
                bool isButton = i < ButtonMask.Length && ButtonMask[i] == 'b';
                sum += ChildHeight(i, minButton);
                children++;
                if (isButton && ++shown >= MinVisibleButtons) break;
            }
            if (children > 1) sum += Spacing * (children - 1);
            return sum;
        }
    }

    /// <summary>버튼 크기(기준 해상도 단위) 명세.</summary>
    public sealed class ButtonSpec
    {
        public string Screen;
        public string Name;
        public float W;
        public float H;
    }

    /// <summary>화면 한 종류의 픽셀 크기와 안전 영역 여백(픽셀).</summary>
    public sealed class ReferenceScreen
    {
        public string Name;
        public int PxW;
        public int PxH;
        public int InsetLeft;
        public int InsetRight;
        public int InsetTop;
        public int InsetBottom;
        /// <summary>화면 밀도(dpi). 터치 목표 크기(dp) 계산에만 쓴다.</summary>
        public float Dpi = 160f;

        public ReferenceScreen(string name, int w, int h, int l, int r, int t, int b, float dpi)
        {
            Name = name;
            PxW = w;
            PxH = h;
            InsetLeft = l;
            InsetRight = r;
            InsetTop = t;
            InsetBottom = b;
            Dpi = dpi;
        }

        public int SafeW { get { return PxW - InsetLeft - InsetRight; } }

        public int SafeH { get { return PxH - InsetTop - InsetBottom; } }
    }

    /// <summary>
    /// 화면 배치 규칙 검사기(순수). GameBootstrap 의 CanvasScaler(기준 1920×1080, Match 0.5 = 가로·세로 비율의 기하 평균)가 만드는 캔버스 크기와,
    /// 각 화면 패널의 수치(코드에서 옮겨 적은 <see cref="Panels"/> 표)로 "패널이 세이프 에어리어에 들어가는가 / 겹치는가 / 쌓은 자식이 패널에 들어가는가 /
    /// 버튼이 최소 터치 크기 이상인가" 를 계산한다. 실제 렌더링 결과가 아니라 코드의 숫자만으로 계산한 것이다(텍스트 줄바꿈·폰트 높이는 모른다).
    /// 표를 코드와 맞추는 일은 하네스(polish_check)가 SourceSnippet 으로 확인한다.
    /// </summary>
    public static class LayoutRules
    {
        public const float RefWidth = 1920f;
        public const float RefHeight = 1080f;
        public const float Match = 0.5f;

        /// <summary>최소 터치 목표(기준 해상도 단위, 하한). 버튼이 이보다 작으면 UiKit 이 키운다.</summary>
        public const float MinTouchRefPx = 48f;

        /// <summary>안드로이드/머티리얼 권장 최소 터치 목표(dp).</summary>
        public const float MinTouchDp = 48f;

        /// <summary>패널 수치 비교 오차(기준 해상도 단위).</summary>
        public const float Eps = 0.5f;

        public static readonly ReferenceScreen Screen16x9 = new ReferenceScreen("1920x1080 (16:9)", 1920, 1080, 0, 0, 0, 0, 160f);

        /// <summary>20:9 폰(2400×1080)에 가로 노치 110px·제스처 바 54px 를 가정한 안전 영역 절단. 밀도 420dpi.</summary>
        public static readonly ReferenceScreen Screen20x9 = new ReferenceScreen("2400x1080 (20:9, safe-area cut)", 2400, 1080, 110, 110, 0, 54, 420f);

        public static readonly float[] UiScales = { 0.9f, 1.0f, 1.1f };

        // ---------------- 캔버스 계산 ----------------

        /// <summary>
        /// CanvasScaler 배율(물리 픽셀 / 캔버스 단위). 기준 해상도를 uiScale 로 나눈 뒤 Match 0.5 로 로그 평균한다.
        /// uiScale 이 크면 같은 화면에서 캔버스가 작아져 UI 가 커진다.
        /// </summary>
        public static float CanvasScale(int pxW, int pxH, float uiScale)
        {
            if (pxW <= 0 || pxH <= 0 || uiScale <= 0f) return 1f;
            double logW = Math.Log(pxW / (RefWidth / uiScale), 2.0);
            double logH = Math.Log(pxH / (RefHeight / uiScale), 2.0);
            double log = logW * (1.0 - Match) + logH * Match;
            return (float)Math.Pow(2.0, log);
        }

        /// <summary>세이프 에어리어(SafeRoot)의 크기와 위치를 캔버스 단위로. 원점은 안전 영역의 왼쪽 아래.</summary>
        public static LayoutRect SafeRect(ReferenceScreen screen, float uiScale)
        {
            float s = CanvasScale(screen.PxW, screen.PxH, uiScale);
            return new LayoutRect(0f, 0f, screen.SafeW / s, screen.SafeH / s);
        }

        /// <summary>패널의 사각형을 부모 사각형(원점 왼쪽 아래) 안의 좌표로 계산한다.</summary>
        public static LayoutRect PanelRect(PanelSpec p, LayoutRect parent)
        {
            float w = p.StretchX ? parent.W + p.W : p.W;
            float ax = p.StretchX ? 0.5f : p.AnchorX;
            float anchorPx = parent.W * ax;
            float anchorPy = parent.H * p.AnchorY;
            float x = anchorPx + p.PosX - p.PivotX * w;
            float y = anchorPy + p.PosY - p.PivotY * p.H;
            return new LayoutRect(x, y, w, p.H);
        }

        /// <summary>패널 이름으로 찾기(없으면 null).</summary>
        public static PanelSpec Find(string name)
        {
            for (int i = 0; i < Panels.Count; i++) if (Panels[i].Name == name) return Panels[i];
            return null;
        }

        /// <summary>패널의 절대 사각형(부모 체인을 따라 올라가며 계산, 안전 영역 좌표). 못 찾으면 W=H=0.</summary>
        public static LayoutRect Absolute(PanelSpec p, LayoutRect safe)
        {
            LayoutRect parentRect = safe;
            if (!string.IsNullOrEmpty(p.Parent))
            {
                PanelSpec parent = Find(p.Parent);
                if (parent == null) return new LayoutRect();
                LayoutRect pr = Absolute(parent, safe);
                parentRect = new LayoutRect(0f, 0f, pr.W, pr.H);
                LayoutRect local = PanelRect(p, parentRect);
                return new LayoutRect(pr.X + local.X, pr.Y + local.Y, local.W, local.H);
            }
            return PanelRect(p, parentRect);
        }

        // ---------------- 검사 ----------------

        /// <summary>
        /// 화면·배율 하나에서 모든 패널을 검사해 위반 문장 목록을 돌려준다(빈 목록 = 통과).
        /// 검사: ① 패널이 부모(최상위는 안전 영역)에 들어가는가 ② 같은 화면의 최상위 패널끼리 겹치지 않는가 ③ 세로 스택의 자식 높이 합이 패널 높이 이내인가.
        /// </summary>
        public static List<string> Check(ReferenceScreen screen, float uiScale)
        {
            List<string> bad = new List<string>();
            LayoutRect safe = SafeRect(screen, uiScale);
            string tag = screen.Name + " x" + uiScale.ToString("0.0");

            for (int i = 0; i < Panels.Count; i++)
            {
                PanelSpec p = Panels[i];
                LayoutRect abs = Absolute(p, safe);
                LayoutRect bounds = safe;
                if (!string.IsNullOrEmpty(p.Parent))
                {
                    PanelSpec parent = Find(p.Parent);
                    bounds = Absolute(parent, safe);
                }
                if (!bounds.Contains(abs, Eps))
                    bad.Add(tag + ": " + p.Screen + "/" + p.Name + " " + abs + " 이(가) " + (string.IsNullOrEmpty(p.Parent) ? "안전 영역 " : "부모 패널 ") + bounds + " 밖으로 나감");
            }

            for (int i = 0; i < Panels.Count; i++)
            {
                PanelSpec a = Panels[i];
                if (!string.IsNullOrEmpty(a.Parent) || a.States == "-") continue;
                for (int j = i + 1; j < Panels.Count; j++)
                {
                    PanelSpec b = Panels[j];
                    if (!string.IsNullOrEmpty(b.Parent) || b.Screen != a.Screen || !a.CoVisible(b)) continue;
                    if (Absolute(a, safe).Overlaps(Absolute(b, safe), Eps))
                        bad.Add(tag + ": " + a.Screen + "/" + a.Name + " 와(과) " + b.Name + " 가 겹침");
                }
            }

            for (int i = 0; i < Stacks.Count; i++)
            {
                StackSpec st = Stacks[i];
                PanelSpec p = Find(st.Panel);
                if (p == null) { bad.Add(tag + ": 스택 " + st.Name + " 의 패널 " + st.Panel + " 없음"); continue; }
                float content = st.ContentHeight();
                if (content > p.H + Eps)
                    bad.Add(tag + ": 스택 " + st.Name + " 내용 높이 " + content.ToString("0") + " > 패널 " + p.Name + " 높이 " + p.H.ToString("0"));
            }
            return bad;
        }

        /// <summary>
        /// 터치 정책 검사: 버튼이 minButton(기준 px)까지 커졌을 때 세로 스택이 패널에 들어가는가. 안 들어가면 Scrollable 로 표시된 스택만 허용하고
        /// (그때도 고정 머리 + 최소 버튼 수는 보여야 한다), 그 밖은 위반. 패널 배치(Check)는 크기가 변하지 않으므로 별도로 이미 통과해야 한다.
        /// </summary>
        public static List<string> CheckTouchStacks(ReferenceScreen screen, float uiScale, float minButton)
        {
            List<string> bad = new List<string>();
            string tag = screen.Name + " x" + uiScale.ToString("0.0") + " 버튼>=" + minButton.ToString("0");
            for (int i = 0; i < Stacks.Count; i++)
            {
                StackSpec st = Stacks[i];
                PanelSpec p = Find(st.Panel);
                if (p == null) continue;
                if (st.GrowsOnMobile) continue;
                float panelH = st.MobileHeightOf != null ? st.MobileHeightOf(minButton) : (st.MobileH > 0f ? st.MobileH : p.H);
                float content = st.ContentHeight(minButton);
                if (content <= panelH + Eps) continue;
                if (!st.Scrollable)
                    bad.Add(tag + ": 스택 " + st.Name + " 내용 높이 " + content.ToString("0") + " > 패널 " + p.Name + " 높이 " + panelH.ToString("0") + " (스크롤 처리 없음)");
                else if (st.MinVisibleHeight(minButton) > panelH + Eps)
                    bad.Add(tag + ": 스크롤 스택 " + st.Name + " 의 고정 머리+버튼 " + st.MinVisibleButtons + "개 높이 " + st.MinVisibleHeight(minButton).ToString("0") + " > 패널 높이 " + panelH.ToString("0"));
            }
            return bad;
        }

        /// <summary>
        /// 루트 선택 화면(모바일): 후보 목록(버튼 3개가 보이는 높이) + 동작 버튼 3개 영역이 패널 높이(880) 안에 들어가는가. 코드의 모바일 분기와 같은 식.
        /// </summary>
        public static float RouteMobileNeeded(float minButton)
        {
            float actions = 3f * minButton + 16f;
            float listMin = 3f * minButton + 16f;
            return 100f + listMin + 12f + actions + 24f;
        }

        /// <summary>이 화면에서 모든 패널이 들어가는 UI 배율만 돌려준다(항상 최소 하나, 없으면 가장 작은 배율).</summary>
        public static List<float> AllowedUiScales(ReferenceScreen screen)
        {
            List<float> ok = new List<float>();
            for (int i = 0; i < UiScales.Length; i++)
                if (Check(screen, UiScales[i]).Count == 0) ok.Add(UiScales[i]);
            if (ok.Count == 0) ok.Add(UiScales[0]);
            return ok;
        }

        /// <summary>
        /// 요청한 UI 배율을 이 화면에서 쓸 수 있는 가장 가까운 배율로 조정한다(요청 값이 허용되면 그대로).
        /// 실행 중에는 화면 크기·안전 영역이 바뀔 수 있으므로 적용 때마다 다시 호출한다.
        /// </summary>
        public static float EffectiveUiScale(float requested, ReferenceScreen screen)
        {
            List<float> ok = AllowedUiScales(screen);
            float best = ok[0];
            for (int i = 1; i < ok.Count; i++)
                if (Math.Abs(ok[i] - requested) < Math.Abs(best - requested)) best = ok[i];
            return best;
        }

        /// <summary>버튼 명세 검사: 높이·너비가 최소 터치 목표(기준 해상도 단위) 미만인 버튼 목록.</summary>
        public static List<string> CheckButtons(IEnumerable<ButtonSpec> buttons)
        {
            List<string> bad = new List<string>();
            foreach (ButtonSpec b in buttons)
                if (b.H < MinTouchRefPx || b.W < MinTouchRefPx)
                    bad.Add(b.Screen + "/" + b.Name + " " + b.W.ToString("0") + "x" + b.H.ToString("0") + " < " + MinTouchRefPx.ToString("0"));
            return bad;
        }

        /// <summary>UiKit.MakeButton 이 쓰는 하한 적용(작은 값은 MinTouchRefPx 로).</summary>
        public static float ClampTouch(float size)
        {
            return size < MinTouchRefPx ? MinTouchRefPx : size;
        }

        // ---------------- 터치 크기(dp) ----------------

        /// <summary>기준 해상도 단위 길이 → dp(안드로이드 160dpi 기준). 캔버스 배율과 화면 밀도를 쓴다.</summary>
        public static float RefPxToDp(float refPx, ReferenceScreen screen, float uiScale)
        {
            float s = CanvasScale(screen.PxW, screen.PxH, uiScale);
            return refPx * s * 160f / screen.Dpi;
        }

        /// <summary>dp → 기준 해상도 단위 길이(이 화면에서 dp 만큼 보이게 하려면 몇 단위가 필요한가).</summary>
        public static float DpToRefPx(float dp, ReferenceScreen screen, float uiScale)
        {
            float s = CanvasScale(screen.PxW, screen.PxH, uiScale);
            return dp * screen.Dpi / 160f / s;
        }

        // ---------------- 코드 수치 표 ----------------

        public static readonly List<PanelSpec> Panels = new List<PanelSpec>();
        public static readonly List<StackSpec> Stacks = new List<StackSpec>();
        public static readonly List<ButtonSpec> Buttons = new List<ButtonSpec>();

        private static void Panel(string screen, string name, float ax, float ay, float px, float py, float w, float h, float x, float y,
            string file, string snippet, string parent, bool stretchX, string states)
        {
            PanelSpec p = new PanelSpec();
            p.Screen = screen; p.Name = name; p.Parent = parent;
            p.AnchorX = ax; p.AnchorY = ay; p.PivotX = px; p.PivotY = py;
            p.W = w; p.H = h; p.PosX = x; p.PosY = y;
            p.StretchX = stretchX; p.States = states;
            p.SourceFile = file; p.SourceSnippet = snippet;
            Panels.Add(p);
        }

        private static void Stack(string name, string panel, float padding, float spacing, params float[] heights)
        {
            StackSpec s = new StackSpec();
            s.Name = name; s.Panel = panel; s.Padding = padding; s.Spacing = spacing;
            s.ChildHeights.AddRange(heights);
            Stacks.Add(s);
        }

        private static void Mask(string stackName, string mask, bool scrollable, float mobileH, bool grows)
        {
            for (int i = 0; i < Stacks.Count; i++)
            {
                if (Stacks[i].Name != stackName) continue;
                Stacks[i].ButtonMask = mask;
                Stacks[i].Scrollable = scrollable;
                Stacks[i].MobileH = mobileH;
                Stacks[i].GrowsOnMobile = grows;
                return;
            }
            throw new InvalidOperationException("no stack " + stackName);
        }

        private static void SetMobile(string stackName, int minVisibleButtons, params float[] heights)
        {
            for (int i = 0; i < Stacks.Count; i++)
            {
                if (Stacks[i].Name != stackName) continue;
                Stacks[i].MinVisibleButtons = minVisibleButtons;
                Stacks[i].MobileChildHeights.AddRange(heights);
                return;
            }
            throw new InvalidOperationException("no stack " + stackName);
        }

        private static void SetMobileHeight(string stackName, Func<float, float> fn)
        {
            for (int i = 0; i < Stacks.Count; i++)
                if (Stacks[i].Name == stackName) { Stacks[i].MobileHeightOf = fn; return; }
            throw new InvalidOperationException("no stack " + stackName);
        }

        private static void Button(string screen, string name, float w, float h)
        {
            ButtonSpec b = new ButtonSpec();
            b.Screen = screen; b.Name = name; b.W = w; b.H = h;
            Buttons.Add(b);
        }

        static LayoutRules()
        {
            // ---- 타이틀 ----
            Panel("Title", "TitleLabel", 0.5f, 1f, 0.5f, 1f, 1600f, 140f, 0f, -70f, "UI/TitleScreen.cs", "new Vector2(1600f, 140f), new Vector2(0f, -70f)", null, false, "*");
            Panel("Title", "TitleSub", 0.5f, 1f, 0.5f, 1f, 1200f, 60f, 0f, -210f, "UI/TitleScreen.cs", "new Vector2(1200f, 60f), new Vector2(0f, -210f)", null, false, "*");
            Panel("Title", "EndingCount", 0.5f, 1f, 0.5f, 1f, 1200f, 50f, 0f, -270f, "UI/TitleScreen.cs", "new Vector2(1200f, 50f), new Vector2(0f, -270f)", null, false, "*");
            Panel("Title", "Menu", 0.5f, 1f, 0.5f, 1f, 560f, 520f, 0f, -335f, "UI/TitleScreen.cs", "new Vector2(560f, 520f), new Vector2(0f, -335f)", null, false, "menu");
            Panel("Title", "Difficulty", 0.5f, 1f, 0.5f, 1f, 1000f, 560f, 0f, -335f, "UI/TitleScreen.cs", "new Vector2(1000f, 560f), new Vector2(0f, -335f)", null, false, "difficulty");
            Panel("Title", "Gallery", 0.5f, 1f, 0.5f, 1f, 1100f, 575f, 0f, -325f, "UI/TitleScreen.cs", "new Vector2(1100f, 575f), new Vector2(0f, -325f)", null, false, "gallery");
            Panel("Title", "Message", 0.5f, 0f, 0.5f, 0f, 1500f, 56f, 0f, 6f, "UI/TitleScreen.cs", "new Vector2(1500f, 56f), new Vector2(0f, 6f)", null, false, "menu");

            // ---- 설정 패널(타이틀·일시정지 공용) ----
            Panel("Settings", "SettingsPanel", 0.5f, 0.5f, 0.5f, 0.5f, 1100f, 780f, 0f, 0f, "UI/SettingsPanel.cs", "new Vector2(1100f, 780f), Vector2.zero", null, false, "*");

            // ---- 일시정지 ----
            Panel("Pause", "PauseMenu", 0.5f, 0.5f, 0.5f, 0.5f, 760f, 600f, 0f, 0f, "UI/PauseMenu.cs", "new Vector2(760f, 600f), Vector2.zero", null, false, "menu");
            Panel("Pause", "PauseConfirm", 0.5f, 0.5f, 0.5f, 0.5f, 900f, 420f, 0f, 0f, "UI/PauseMenu.cs", "new Vector2(900f, 420f), Vector2.zero", null, false, "confirm");

            // ---- 주인공 생성 ----
            Panel("Create", "Form", 0f, 0.5f, 0f, 0.5f, 980f, 880f, 24f, 0f, "UI/CharacterCreateScreen.cs", "new Vector2(980f, 880f), new Vector2(24f, 0f)", null, false, "*");
            Panel("Create", "Preview", 1f, 0.5f, 1f, 0.5f, 640f, 880f, -24f, 0f, "UI/CharacterCreateScreen.cs", "new Vector2(640f, 880f), new Vector2(-24f, 0f)", null, false, "*");

            // ---- 게임 화면(낮 단계 / 밤 단계) ----
            Panel("Game", "Hud", 0.5f, 1f, 0.5f, 1f, 0f, 96f, 0f, 0f, "UI/GameScreen.cs", "hud.rectTransform.sizeDelta = new Vector2(0f, 96f)", null, true, "*");
            Panel("Game", "Content", 0.5f, 0.5f, 0.5f, 0.5f, 1100f, 760f, 160f, -30f, "UI/GameScreen.cs", "new Vector2(1100f, 760f), new Vector2(160f, -30f)", null, false, "day");
            Panel("Game", "ContentNight", 0.5f, 0.5f, 0.5f, 0.5f, 840f, 790f, -120f, -48f, "UI/GameScreen.cs", "new Vector2(840f, 790f), new Vector2(-120f, -48f)", null, false, "night");
            Panel("Game", "Relation", 1f, 0.5f, 1f, 0.5f, 520f, 790f, -20f, -48f, "UI/RelationPanel.cs", "new Vector2(520f, 790f), new Vector2(-20f, -48f)", null, false, "night");

            // ---- VN ----
            Panel("Vn", "Box", 0.5f, 0f, 0.5f, 0f, -80f, 270f, 0f, 24f, "UI/VnStage.cs", "_box.sizeDelta = new Vector2(-80f, 270f)", null, true, "*");
            Panel("Vn", "Choices", 0.5f, 0f, 0.5f, 0f, 1200f, 480f, 0f, 310f, "UI/VnStage.cs", "new Vector2(1200f, 480f), new Vector2(0f, 310f)", null, false, "*");

            // ---- 루트 선택 ----
            Panel("Route", "Panel", 0.5f, 0.5f, 0.5f, 0.5f, 1560f, 880f, 0f, 0f, "UI/RouteSelectScreen.cs", "PanelHeight = 880f", null, false, "*");
            Panel("Route", "List", 0f, 1f, 0f, 1f, 880f, 520f, 40f, -100f, "UI/RouteSelectScreen.cs", "new Vector2(880f, 520f), new Vector2(40f, -100f)", "Panel", false, "*");
            Panel("Route", "Actions", 0f, 0f, 0f, 0f, 880f, 236f, 40f, 24f, "UI/RouteSelectScreen.cs", "new Vector2(880f, 236f), new Vector2(40f, 24f)", "Panel", false, "*");
            Panel("Route", "Explain", 1f, 1f, 1f, 1f, 560f, 520f, -40f, -100f, "UI/RouteSelectScreen.cs", "new Vector2(560f, 520f), new Vector2(-40f, -100f)", "Panel", false, "*");
            Panel("Route", "Message", 1f, 0f, 1f, 0f, 560f, 220f, -40f, 24f, "UI/RouteSelectScreen.cs", "new Vector2(560f, 220f), new Vector2(-40f, 24f)", "Panel", false, "*");
            Panel("Route", "ConfirmDialog", 0.5f, 0.5f, 0.5f, 0.5f, 1100f, 600f, 0f, 0f, "UI/RouteSelectScreen.cs", "new Vector2(1100f, 600f), Vector2.zero", null, false, "-");

            // ---- 엔딩 ----
            Panel("End", "Panel", 0.5f, 0.5f, 0.5f, 0.5f, 1200f, 780f, 0f, 0f, "UI/EndScreen.cs", "new Vector2(1200f, 780f), Vector2.zero", null, false, "*");
            Panel("DemoEnd", "Panel", 0.5f, 0.5f, 0.5f, 0.5f, 1200f, 780f, 0f, 0f, "UI/DemoEndScreen.cs", "new Vector2(1200f, 780f), Vector2.zero", null, false, "*");

            // ---- 세로 스택(코드의 레이아웃 그룹 수치로 계산한 최악 높이) ----
            // 밤 메뉴: 제목 70 + 정보 줄 44 + 버튼 최대 10개(복구/동의 인라인 2개 포함, 높이 48, 간격 8)
            Stack("밤 메뉴(인라인 항목 2개)", "ContentNight", 36f, 8f, 70f, 44f, 48f, 48f, 48f, 48f, 48f, 48f, 48f, 48f, 48f, 48f);
            // 밤 메뉴: 항목 없음 — 제목 70 + 정보 44 + 버튼 8개(높이 56, 간격 14)
            Stack("밤 메뉴(기본)", "ContentNight", 36f, 14f, 70f, 44f, 56f, 56f, 56f, 56f, 56f, 56f, 56f, 56f);
            // 아침 메뉴: 제목 70 + 정보 2줄 + 버튼 4개(높이 72, 간격 14)
            Stack("아침 메뉴", "Content", 36f, 14f, 70f, 44f, 44f, 72f, 72f, 72f, 72f);
            // 주인공 생성 폼(제목 48+18, 라벨 28+18 ×4, 입력 70, 성별 64, 종 60×2, 외형 72, 오류 줄 40, 버튼 76; 간격 10)
            Stack("주인공 생성 폼", "Form", 28f, 10f, 66f, 46f, 70f, 46f, 64f, 46f, 60f, 60f, 46f, 72f, 40f, 76f);
            // 타이틀 메뉴: 버튼 6개(84, 84, 72×4), 간격 12
            Stack("타이틀 메뉴", "Menu", 0f, 12f, 84f, 84f, 72f, 72f, 72f, 72f);
            // 타이틀 난이도: 제목 64 + 버튼 84×3 + 뒤로 64, 간격 16
            Stack("타이틀 난이도", "Difficulty", 0f, 16f, 64f, 84f, 84f, 84f, 64f);
            // 타이틀 갤러리: 패딩 30, 제목 60 + 본문 360 + 뒤로 64, 간격 10
            Stack("타이틀 갤러리", "Gallery", 30f, 10f, 60f, 360f, 64f);
            // 설정 패널: 제목 56 + 슬라이더 줄 5 + 언어·UI 크기·전체 화면 줄 3 + 안내 36 + 닫기 64 (줄 56, 간격 12)
            Stack("설정 패널", "SettingsPanel", 24f, 12f, 56f, 56f, 56f, 56f, 56f, 56f, 56f, 56f, 56f, 36f, 64f);
            // 일시정지: 제목 56 + 버튼 4개(72) + 안내 줄 60, 간격 14
            Stack("일시정지 메뉴", "PauseMenu", 24f, 14f, 56f, 72f, 72f, 72f, 72f, 60f);
            // 루트 선택 후보 목록: 6명 × 78, 간격 8
            Stack("루트 선택 목록", "List", 0f, 8f, 78f, 78f, 78f, 78f, 78f, 78f);
            // 루트 선택 동작 버튼: 68, 68, 60, 간격 8
            Stack("루트 선택 동작", "Actions", 0f, 8f, 68f, 68f, 60f);
            // 선택지(VN): 최대 5개 × 78, 간격 16
            Stack("VN 선택지", "Choices", 0f, 16f, 78f, 78f, 78f, 78f, 78f);

            // 일시정지 확인 대화(모바일에서는 버튼 2개가 커지므로 패널을 540 으로 키운다)
            Stack("일시정지 확인", "PauseConfirm", 36f, 14f, 64f, 110f, 64f, 64f);
            // 밤 선택 목록(대화·선물·데이트·이벤트 선택, 페이지 6개 + 이전/다음 + 뒤로)
            Stack("밤 선택 목록", "ContentNight", 36f, 14f, 70f, 44f, 56f, 56f, 56f, 56f, 56f, 56f, 56f, 56f);

            // 터치 정책: 버튼 자식 표시('b'), 스크롤 처리 여부, 모바일 패널 크기
            Mask("밤 메뉴(인라인 항목 2개)", "xxbbbbbbbbbb", true, 0f, false);
            Mask("밤 메뉴(기본)", "xxbbbbbbbb", true, 0f, false);
            Mask("밤 선택 목록", "xxbbbbbbbb", true, 0f, false);
            Mask("아침 메뉴", "xxxbbbb", true, 0f, false);
            Mask("주인공 생성 폼", "xxxxbxbbxbxb", true, 0f, false);
            Mask("타이틀 메뉴", "bbbbbb", true, 0f, false);
            Mask("타이틀 난이도", "xbbbb", true, 0f, false);
            Mask("타이틀 갤러리", "xxb", true, 0f, false);
            SetMobile("타이틀 갤러리", 1, 60f, 270f, 64f); // 모바일: 본문 글자 24 로 줄여 높이 270(9줄)
            SetMobileHeight("루트 선택 목록", delegate (float m) { return 880f - 100f - 24f - (3f * m + 16f) - 12f; });
            Mask("설정 패널", "xxxxxxbbbxb", true, 0f, false);
            Mask("일시정지 메뉴", "xbbbbx", true, 0f, false);
            Mask("일시정지 확인", "xxbb", false, 540f, false);
            Mask("루트 선택 목록", "bbbbbb", true, 0f, false);
            Mask("루트 선택 동작", "bbb", false, 0f, true);
            Mask("VN 선택지", "bbbbb", true, 540f, false);

            // ---- 버튼 크기(코드의 MakeButton / AddButton 수치) ----
            Button("Title", "메뉴 큰 버튼", 520f, 84f);
            Button("Title", "메뉴 보조 버튼", 520f, 72f);
            Button("Title", "난이도", 900f, 84f);
            Button("Title", "뒤로", 400f, 64f);
            Button("Settings", "줄 버튼", 340f, 56f);
            Button("Settings", "닫기", 520f, 64f);
            Button("Pause", "메뉴 버튼", 640f, 72f);
            Button("Create", "성별", 200f, 64f);
            Button("Create", "종", 200f, 60f);
            Button("Create", "외형", 96f, 72f);
            Button("Create", "뒤로/확인", 260f, 76f);
            Button("Game", "기본 버튼", 980f, 72f);
            Button("Game", "밤 버튼", 740f, 56f);
            Button("Game", "밤 버튼(인라인 항목)", 740f, 48f);
            Button("Game", "페이지 이동", 390f, 56f);
            Button("Vn", "자동/스킵", 150f, 56f);
            Button("Vn", "선택지", 1100f, 78f);
            Button("Route", "후보", 880f, 78f);
            Button("Route", "동작", 880f, 68f);
            Button("Route", "보류", 880f, 60f);
            Button("Route", "대화 상자", 700f, 64f);
            Button("End", "타이틀로", 600f, 76f);
            Button("DemoEnd", "스토어", 760f, 76f);
            Button("DemoEnd", "타이틀로", 600f, 76f);
        }
    }
}
