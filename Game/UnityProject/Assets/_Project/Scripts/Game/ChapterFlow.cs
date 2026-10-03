using System;

namespace MorpheHouse.GameFlow
{
    /// <summary>챕터 전환 시 아침 화면이 해야 할 일의 종류.</summary>
    public enum ChapterStepKind
    {
        /// <summary>할 일 없음(바로 아침 메뉴).</summary>
        None = 0,
        /// <summary>이전 챕터의 마무리 노드(Ch&lt;N-1&gt;_Outro)를 재생.</summary>
        Outro = 1,
        /// <summary>이번 챕터의 도입 노드(Ch&lt;N&gt;_Intro)를 재생.</summary>
        Intro = 2,
        /// <summary>도입 노드가 없을 때의 기존 정보 카드.</summary>
        InfoCard = 3,
        /// <summary>자동 재생 공통 장면(CE02..CE08, Ch5_Show 등). 규칙은 balance/scene_rules.csv(<see cref="SceneGate"/>).</summary>
        CommonScene = 4
    }

    /// <summary>챕터 흐름 판단 결과 한 단계.</summary>
    public sealed class ChapterStep
    {
        public ChapterStepKind Kind = ChapterStepKind.None;
        /// <summary>재생할 노드 이름(Outro / Intro 일 때만, 아니면 빈 문자열).</summary>
        public string Node = string.Empty;

        public static readonly ChapterStep NoStep = new ChapterStep();

        public static ChapterStep Of(ChapterStepKind kind, string node)
        {
            ChapterStep s = new ChapterStep();
            s.Kind = kind;
            s.Node = node ?? string.Empty;
            return s;
        }
    }

    /// <summary>
    /// 챕터 도입/마무리 노드 재생 규칙(순수). 상태(챕터·챕터 내 일차·방문 기록)만으로 판단하므로
    /// 아침 저장 → 불러오기 후에도 아직 보지 않은 마무리/도입 장면이 이어서 재생된다.
    /// 노드 이름 규칙: Ch&lt;N&gt;_Intro / Ch&lt;N&gt;_Outro (N = 1..8). 없는 노드는 조용히 건너뛴다.
    /// </summary>
    public static class ChapterFlow
    {
        public static string IntroNode(int chapter)
        {
            return "Ch" + chapter + "_Intro";
        }

        public static string OutroNode(int chapter)
        {
            return "Ch" + chapter + "_Outro";
        }

        /// <summary>
        /// 아침 화면을 열 때 다음에 할 일을 정한다. 반복 호출해도(재생 후 방문 기록이 생기므로) 마무리 → 도입 → 정보 카드 → 없음 순으로 진행한다.
        /// 챕터의 첫날(dayInChapter == 1)에만 동작한다.
        /// </summary>
        /// <param name="introPending">DayCycle.ChapterIntroPending(정보 카드를 아직 보여 주지 않았는가).</param>
        public static ChapterStep NextMorningStep(int chapter, int dayInChapter, bool introPending,
            Func<string, bool> nodeExists, Func<string, bool> hasVisited)
        {
            if (dayInChapter != 1) return ChapterStep.NoStep;
            Func<string, bool> exists = nodeExists ?? delegate (string n) { return false; };
            Func<string, bool> visited = hasVisited ?? delegate (string n) { return false; };

            if (chapter > 1)
            {
                string outro = OutroNode(chapter - 1);
                if (exists(outro) && !visited(outro)) return ChapterStep.Of(ChapterStepKind.Outro, outro);
            }
            string intro = IntroNode(chapter);
            if (exists(intro) && !visited(intro)) return ChapterStep.Of(ChapterStepKind.Intro, intro);
            if (introPending) return ChapterStep.Of(ChapterStepKind.InfoCard, string.Empty);
            return ChapterStep.NoStep;
        }

        /// <summary>
        /// 지금 자동 재생할 공통 장면 한 단계. 게이트가 없거나 대상이 없으면 NoStep. 재생 뒤 방문 기록이 남으면 같은 장면은 다시 나오지 않으므로
        /// 호스트는 NoStep 이 나올 때까지 반복 호출할 수 있다. 아침의 챕터 도입/마무리(NextMorningStep)가 끝난 뒤에 호출한다.
        /// </summary>
        public static ChapterStep NextCommonScene(SceneGate gate, SceneSlot slot)
        {
            if (gate == null) return ChapterStep.NoStep;
            SceneRule r = gate.NextAutoScene(slot);
            return r == null ? ChapterStep.NoStep : ChapterStep.Of(ChapterStepKind.CommonScene, r.Node);
        }

        /// <summary>마지막 챕터의 마무리(Ch&lt;last&gt;_Outro)가 아직 남았으면 그 노드 이름, 없거나 이미 봤으면 null. 엔딩 선택 직전에 호출한다.</summary>
        public static string FinalOutro(int lastChapter, Func<string, bool> nodeExists, Func<string, bool> hasVisited)
        {
            string outro = OutroNode(lastChapter);
            bool exists = nodeExists != null && nodeExists(outro);
            bool visited = hasVisited != null && hasVisited(outro);
            return exists && !visited ? outro : null;
        }
    }
}
