namespace MorpheHouse.UI
{
    /// <summary>
    /// 화면 문자열 모음. 각 속성의 이름이 곧 문자열 키이고, 코드에 적힌 값은 한국어 기본값(CSV 가 없어도 게임이 돌아가게 하는 마지막 안전망)이다.
    /// 실제 표시 문자열은 <see cref="UiStrings.Current"/>(Resources/Localization/ui_ko.csv, ui_en.csv)가 현재 언어로 돌려주며,
    /// 키가 없으면 한국어 → 이 기본값 순으로 대체한다. 새 문자열은 여기에 속성을 추가하고 두 CSV 에 같은 키를 넣는다
    /// (tools/csharp_check/polish_check 가 키 누락·한국어 기본값 불일치를 검사한다).
    /// </summary>
    public static class UiText
    {
        private static string T(string korean, [System.Runtime.CompilerServices.CallerMemberName] string key = null)
        {
            return MorpheHouse.GameFlow.UiStrings.Current.Get(key, korean);
        }

        public static string Title { get { return T("달빛 모퉁이의 수인들"); } }
        public static string Subtitle { get { return T("가칭 · 프로토타입 v0.1"); } }

        public static string NewGame { get { return T("새 게임"); } }
        public static string Continue { get { return T("이어하기"); } }
        public static string DevAutoPlay { get { return T("자동 플레이 검증 (개발용)"); } }
        public static string Quit { get { return T("종료"); } }
        public static string Back { get { return T("뒤로"); } }
        public static string Settings { get { return T("설정"); } }
        public static string Close { get { return T("닫기"); } }
        public static string Cancel { get { return T("취소"); } }

        public static string ChooseDifficulty { get { return T("난이도를 선택하세요"); } }
        public static string DifficultyStory { get { return T("스토리 — 느긋하게 이야기 중심으로"); } }
        public static string DifficultyNormal { get { return T("노멀 — 권장 밸런스"); } }
        public static string DifficultyHard { get { return T("하드 — 경영 압박, 관계도 쉽게 식지 않음"); } }

        public static string Morning { get { return T("아침"); } }
        public static string Business { get { return T("영업"); } }
        public static string Night { get { return T("밤"); } }
        public static string Settlement { get { return T("정산"); } }

        public static string StartBusiness { get { return T("영업 시작"); } }
        public static string UpgradeShop { get { return T("가게 확장"); } }
        public static string UpgradeShopCap { get { return T("가게 확장 (이번 챕터 상한 도달)"); } }
        public static string Save { get { return T("저장 (슬롯 1)"); } }
        public static string ToTitle { get { return T("타이틀로"); } }
        public static string Saved { get { return T("저장했습니다."); } }
        public static string SaveFailed { get { return T("저장에 실패했습니다."); } }

        public static string Rest { get { return T("휴식 (피로 -25)"); } }
        public static string Research { get { return T("메뉴 연구 (내일 만족도 +3%p)"); } }
        public static string FinishNight { get { return T("밤 마무리 → 정산"); } }
        public static string ToNight { get { return T("밤으로"); } }
        public static string NextDay { get { return T("다음 날"); } }

        public static string Fast { get { return T("2배속"); } }
        public static string Normal { get { return T("1배속"); } }

        public static string EndTitle { get { return T("프로토타입 종료"); } }
        public static string LoadNotFound { get { return T("불러올 저장 데이터가 없습니다."); } }
        public static string LoadTampered { get { return T("저장 파일이 손상되었거나 변경되어 불러올 수 없습니다."); } }
        public static string LoadCorrupt { get { return T("저장 파일을 읽을 수 없습니다."); } }
        public static string LoadRestored { get { return T("손상된 저장을 백업에서 복구했습니다."); } }

        // ---- 2단계: 주인공 생성 ----
        public static string CreateTitle { get { return T("주인공 만들기"); } }
        public static string CreateName { get { return T("이름 (최대 12자)"); } }
        public static string CreateNameHint { get { return T("이름을 입력하세요"); } }
        public static string CreateGender { get { return T("성별"); } }
        public static string CreateSpecies { get { return T("종 (임시 선택지)"); } }
        public static string CreateAppearance { get { return T("외형 프리셋"); } }
        public static string CreatePreviewTitle { get { return T("NPC 가 나를 부르는 말"); } }
        public static string CreateConfirm { get { return T("이 모습으로 시작"); } }

        // ---- 2단계: 밤 메뉴 ----
        public static string NightTalk { get { return T("대화 (누구와)"); } }
        public static string NightGift { get { return T("선물 (누구에게)"); } }
        public static string NightDate { get { return T("데이트 (누구와)"); } }
        public static string NightEvent { get { return T("개인 이벤트"); } }
        public static string NightRouteLock { get { return T("루트 확정"); } }
        public static string PickWho { get { return T("누구와 할까요?"); } }
        public static string PickGift { get { return T("어떤 선물을 줄까요?"); } }
        public static string PickSpot { get { return T("어디로 갈까요?"); } }
        public static string PickEvent { get { return T("개인 이벤트 (★ = 볼 수 있음)"); } }
        public static string PrevPage { get { return T("이전"); } }
        public static string NextPage { get { return T("다음"); } }
        public static string RouteLockTitle { get { return T("루트 확정"); } }
        public static string RouteLockWarn { get { return T("한 사람과의 길을 선택하면 되돌릴 수 없고, 다른 인물과의 관계는 더 이상 변하지 않습니다."); } }
        public static string RouteLockConfirm { get { return T("확정한다"); } }
        public static string RouteLockLater { get { return T("보류"); } }

        // ---- 2단계: VN ----
        public static string VnAuto { get { return T("자동"); } }
        public static string VnSkip { get { return T("스킵"); } }

        // ---- 2단계: 설정 ----
        public static string SettingsTitle { get { return T("설정"); } }
        public static string SettingsTextSpeed { get { return T("텍스트 속도"); } }
        public static string SettingsVolume { get { return T("마스터 볼륨"); } }
        public static string SettingsLocale { get { return T("언어"); } }
        public static string SettingsInstant { get { return T("즉시"); } }

        // ---- 3단계: 루트 선택 · 엔딩 · 갤러리 ----
        public static string RouteSelectTitle { get { return T("루트 선택"); } }
        public static string RouteSelectConfirm { get { return T("이 선택으로 확정"); } }
        public static string RouteSelectCommunity { get { return T("정하지 않고 계속 (커뮤니티 엔딩)"); } }
        public static string RouteSelectHold { get { return T("보류 (뒤로)"); } }
        public static string RouteSelectLater { get { return T("나중에 정하기"); } }
        public static string RoutePromptBody { get { return T("루트를 확정할 수 있는 인물이 있습니다. 지금 정할까요?"); } }
        public static string OpenRouteSelect { get { return T("루트 선택 화면 열기"); } }
        public static string ExtraScenes { get { return T("이어지는 장면"); } }
        public static string EndScreenTitle { get { return T("엔딩"); } }
        public static string Gallery { get { return T("엔딩 갤러리"); } }
        public static string GalleryTitle { get { return T("엔딩 갤러리 (도달한 엔딩)"); } }

        // ---- 5단계: 일시정지 · 설정 · 타이틀 ----
        public static string PauseTitle { get { return T("일시정지"); } }
        public static string Resume { get { return T("계속하기"); } }
        public static string PauseSave { get { return T("저장하기 (아침에만)"); } }
        public static string PauseSaveMorningOnly { get { return T("저장은 아침에만 할 수 있어요. 다음 아침에 저장해 주세요."); } }
        public static string ReturnToTitle { get { return T("타이틀로 돌아가기"); } }
        public static string ReturnConfirmTitle { get { return T("타이틀로 돌아갈까요?"); } }
        public static string ReturnConfirmBody { get { return T("저장하지 않은 진행은 사라집니다. 마지막 아침 자동 저장은 남아 있습니다."); } }
        public static string ReturnConfirmYes { get { return T("돌아간다"); } }
        public static string SettingsBgm { get { return T("BGM 볼륨"); } }
        public static string SettingsSe { get { return T("효과음 볼륨"); } }
        public static string SettingsAutoSpeed { get { return T("자동 진행 속도"); } }
        public static string SettingsUiScale { get { return T("UI 크기"); } }
        public static string SettingsFullscreen { get { return T("전체 화면"); } }
        public static string SettingsOn { get { return T("켜짐"); } }
        public static string SettingsOff { get { return T("꺼짐"); } }
        public static string SettingsCharsPerSec { get { return T("{0}자/초"); } }
        public static string SettingsSeconds { get { return T("{0}초"); } }
        public static string SettingsPercent { get { return T("{0}%"); } }
        public static string SettingsUiScaleUnavailable { get { return T("이 화면 크기에서는 선택할 수 없는 크기예요."); } }
        public static string SettingsDialogueNote { get { return T("대사 본문은 아직 한국어만 제공됩니다. 영어 번역은 준비 중입니다."); } }
        public static string PauseButton { get { return T("메뉴"); } }
        public static string TitleEndingsReached { get { return T("도달한 엔딩 {0} / {1}"); } }

        /// <summary>1단계 데모용 대사(2단계에서 Yarn Spinner 대사로 대체).</summary>
        public static string HowlLine { get { return T("…왔어? 의자, 닦아놨다."); } }
    }
}
