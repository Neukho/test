namespace MorpheHouse.GameFlow
{
    /// <summary>뒤로가기(Esc / Android 뒤로 키)를 눌렀을 때 현재 화면이 할 일.</summary>
    public enum BackAction
    {
        /// <summary>아무것도 하지 않는다(타이틀 최상위·엔딩 화면 등).</summary>
        None,
        /// <summary>일시정지 메뉴를 연다(게임 화면·VN).</summary>
        OpenPause,
        /// <summary>일시정지 메뉴를 닫고 계속한다.</summary>
        ClosePause,
        /// <summary>일시정지 메뉴 안의 하위 패널(설정)을 닫고 메뉴로 돌아간다.</summary>
        PauseBackToMenu,
        /// <summary>일시정지 메뉴의 확인 상자(타이틀로 돌아갈까요?)를 닫는다.</summary>
        CloseConfirm,
        /// <summary>화면 자체가 뒤로가기를 처리한다(타이틀 하위 패널 → 메뉴, 주인공 생성 → 타이틀, 루트 선택 → 보류).</summary>
        ScreenHandles
    }

    /// <summary>화면 종류(뒤로가기 규칙 판정용). 실제 화면 클래스와 1:1 로 대응한다.</summary>
    public enum BackScreen
    {
        Title,
        Create,
        Game,
        Vn,
        RouteSelect,
        End
    }

    /// <summary>
    /// 뒤로가기 키의 동작 결정(순수). 우선순위: 일시정지 메뉴가 열려 있으면 그 안에서 한 단계씩 닫고, 아니면 화면이 처리할 수 있으면 화면이,
    /// 게임 진행 화면(Game/Vn)이면 일시정지 메뉴를 연다. 타이틀 최상위와 엔딩 화면에서는 아무 일도 하지 않는다(실수로 종료되지 않게).
    /// </summary>
    public static class BackKeyRules
    {
        public static BackAction Decide(BackScreen screen, bool pauseOpen, bool pauseSubPanelOpen, bool pauseConfirmOpen, bool screenCanHandle)
        {
            if (pauseOpen)
            {
                if (pauseConfirmOpen) return BackAction.CloseConfirm;
                if (pauseSubPanelOpen) return BackAction.PauseBackToMenu;
                return BackAction.ClosePause;
            }
            if (screenCanHandle) return BackAction.ScreenHandles;
            if (screen == BackScreen.Game || screen == BackScreen.Vn) return BackAction.OpenPause;
            return BackAction.None;
        }

        /// <summary>일시정지 메뉴를 열 수 있는 화면인가(타이틀·주인공 생성·엔딩에서는 열지 않는다).</summary>
        public static bool CanPause(BackScreen screen)
        {
            return screen == BackScreen.Game || screen == BackScreen.Vn;
        }

        /// <summary>
        /// 수동 저장 가능 여부와 사유. 저장은 게임 화면의 아침 단계에서만 된다(영업·밤·정산·VN 장면 중에는 불가).
        /// false 면 호출자가 UiText.PauseSaveMorningOnly 안내를 보여 준다.
        /// </summary>
        public static bool CanSaveNow(BackScreen screen, bool isMorning, bool sceneRunning)
        {
            return screen == BackScreen.Game && isMorning && !sceneRunning;
        }
    }
}
