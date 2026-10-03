namespace MorpheHouse.GameFlow
{
    /// <summary>
    /// 대사 커맨드 중 "연출" 쪽(스탠딩·CG·배경)을 받는 쪽. VN 화면이 구현하고, 순수 로직은 이 인터페이스로만 호출한다.
    /// 인자는 이미 ArtKeys 표기(예: expression "Smile", place "Lighthouse", time "Night")로 정규화되어 들어온다.
    /// </summary>
    public interface IVnCommandSink
    {
        void ShowStanding(string charId, string expression, string outfit);

        void HideStanding(string charId);

        /// <summary>CG 표시. number 는 1~6(ArtKeys.Cg).</summary>
        void ShowCg(string charId, int number);

        /// <summary>공통(인물 아닌) CG 표시. key 는 대문자 정규화된 키(HAREM_E1, COMMON_CE03 ...).</summary>
        void ShowCommonCg(string key);

        void HideCg();

        void SetBackground(string place, string time);
    }

    /// <summary>
    /// 선택 구현: 스크립트가 의상을 지정하지 않은 &lt;&lt;show_standing&gt;&gt; 를 받는 쪽(5단계). 구현체는 <see cref="OutfitPolicy"/> 로 의상을 고른다.
    /// IVnCommandSink 만 구현한 쪽(테스트·구형 호스트)에는 기본 의상 Casual 로 ShowStanding 이 호출되어 동작이 바뀌지 않는다.
    /// 의상이 명시된 경우에는 이 인터페이스를 거치지 않고 항상 ShowStanding 으로 간다.
    /// </summary>
    public interface IVnOutfitSink
    {
        void ShowStandingAuto(string charId, string expression);
    }

    /// <summary>아무것도 하지 않는 구현(VN 화면이 없을 때·테스트).</summary>
    public sealed class NullVnSink : IVnCommandSink
    {
        public void ShowStanding(string charId, string expression, string outfit) { }

        public void HideStanding(string charId) { }

        public void ShowCg(string charId, int number) { }

        public void ShowCommonCg(string key) { }

        public void HideCg() { }

        public void SetBackground(string place, string time) { }
    }
}
