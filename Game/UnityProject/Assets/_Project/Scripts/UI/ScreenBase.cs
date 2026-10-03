using System.Collections.Generic;
using UnityEngine;

namespace MorpheHouse.UI
{
    /// <summary>화면 한 장. 전체 화면용 루트(배경)와 세이프 에어리어 루트(상호작용 UI)에 각각 자식을 만든다.</summary>
    public abstract class ScreenBase
    {
        protected readonly AppContext Ctx;
        protected RectTransform FullRoot;
        protected RectTransform SafeRoot;

        public bool IsVisible { get; private set; }

        protected ScreenBase(AppContext ctx)
        {
            Ctx = ctx;
        }

        public void Build(RectTransform fullParent, RectTransform safeParent)
        {
            FullRoot = UiKit.NewRect(GetType().Name + "_Full", fullParent);
            UiKit.Stretch(FullRoot);
            SafeRoot = UiKit.NewRect(GetType().Name + "_Safe", safeParent);
            UiKit.Stretch(SafeRoot);
            OnBuild();
            SetActive(false);
        }

        protected abstract void OnBuild();

        protected virtual void OnShow() { }

        protected virtual void OnHide() { }

        /// <summary>
        /// 뒤로가기(Esc / Android 뒤로 키)를 이 화면이 직접 처리하는가. 처리했으면 true(예: 타이틀 하위 패널 → 메뉴, 주인공 생성 → 타이틀).
        /// 처리하지 않으면 false 를 돌려주고, 게임 진행 화면이면 일시정지 메뉴가 열린다.
        /// </summary>
        public virtual bool HandleBack() { return false; }

        /// <summary>HandleBack 이 true 를 돌려줄 수 있는 상태인가(순수 BackKeyRules 판정 입력). 부작용 없이 조회만 한다.</summary>
        public virtual bool CanHandleBack { get { return false; } }

        /// <summary>언어가 바뀌었을 때 호출된다. 표의 글자와 같은 라벨은 이미 LocalizedLabel 이 바꿨으므로, 동적으로 만든 문구만 다시 쓰면 된다.</summary>
        public virtual void OnLocaleChanged() { }

        /// <summary>보이는 동안 매 프레임 호출된다(GameBootstrap.Update → AppContext.Tick → ScreenManager.Tick).</summary>
        public virtual void Tick(float deltaTime) { }

        public void Show()
        {
            SetActive(true);
            IsVisible = true;
            OnShow();
        }

        public void Hide()
        {
            OnHide();
            IsVisible = false;
            SetActive(false);
        }

        private void SetActive(bool active)
        {
            FullRoot.gameObject.SetActive(active);
            SafeRoot.gameObject.SetActive(active);
        }
    }

    /// <summary>한 번에 한 화면만 보이도록 전환을 관리한다.</summary>
    public sealed class ScreenManager
    {
        private readonly List<ScreenBase> _screens = new List<ScreenBase>();
        private ScreenBase _current;

        /// <summary>화면이 바뀐 직후(새 화면의 Show 뒤) 호출된다. BGM 선택 등에 쓴다.</summary>
        public event System.Action<ScreenBase> Changed;

        public void Register(ScreenBase screen)
        {
            _screens.Add(screen);
        }

        public ScreenBase Current { get { return _current; } }

        public void Tick(float deltaTime)
        {
            if (_current != null && _current.IsVisible) _current.Tick(deltaTime);
        }

        public void Show(ScreenBase screen)
        {
            if (_current != null) _current.Hide();
            _current = screen;
            _current.Show();
            System.Action<ScreenBase> h = Changed;
            if (h != null) h(screen);
        }
    }
}
