using MorpheHouse.GameFlow;
using TMPro;
using UnityEngine;

namespace MorpheHouse.UI
{
    /// <summary>
    /// 문자열 표(UiStrings)의 값과 정확히 같은 글자로 만들어진 라벨에 붙어, 언어가 바뀌면 같은 키의 새 언어 글자로 다시 쓴다.
    /// UiKit.Label 이 생성 시 자동으로 붙이므로 화면 코드는 UiText.X 를 그대로 넘기기만 하면 된다.
    /// 라벨이 그 뒤에 다른 글자로 바뀌었다면(동적 문구) 건드리지 않는다 — 그런 문구는 화면이 다시 그려질 때 새 언어로 만들어진다.
    /// </summary>
    public sealed class LocalizedLabel : MonoBehaviour
    {
        private TMP_Text _text;
        private string _key;
        private string _applied;

        /// <summary>현재 글자가 표의 어떤 키 값과 같으면 그 키로 묶는다(없으면 아무것도 붙이지 않는다).</summary>
        public static void Attach(TMP_Text text)
        {
            if (text == null) return;
            string key = UiStrings.Current.KeyOfText(text.text);
            if (key == null) return;
            LocalizedLabel l = text.gameObject.AddComponent<LocalizedLabel>();
            l._text = text;
            l._key = key;
            l._applied = text.text;
        }

        /// <summary>글자를 직접 바꾼 뒤 키를 다시 맞춘다(UiKit.SetButtonLabel 용). 표에 없는 글자면 묶음을 푼다.</summary>
        public static void Rebind(TMP_Text text)
        {
            if (text == null) return;
            LocalizedLabel l = text.GetComponent<LocalizedLabel>();
            string key = UiStrings.Current.KeyOfText(text.text);
            if (key == null)
            {
                if (l != null) l._key = null;
                return;
            }
            if (l == null) l = text.gameObject.AddComponent<LocalizedLabel>();
            l._text = text;
            l._key = key;
            l._applied = text.text;
        }

        /// <summary>이 라벨을 현재 언어로 다시 쓴다.</summary>
        public void Refresh()
        {
            if (_text == null || _key == null) return;
            if (_text.text != _applied)
            {
                _key = null; // 그 사이에 다른 글자로 바뀌었다.
                return;
            }
            string next = UiStrings.Current.Get(_key);
            _text.text = next;
            _applied = next;
        }

        /// <summary>씬의 모든 라벨(비활성 포함)을 새로 쓴다. 언어를 바꿀 때 한 번 호출한다.</summary>
        public static int RefreshAll()
        {
            LocalizedLabel[] all = FindObjectsByType<LocalizedLabel>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++) all[i].Refresh();
            return all.Length;
        }
    }

    /// <summary>UI 효과음 재생 통로(AudioService 가 연결). 연결 전이거나 소리가 없으면 아무 일도 하지 않는다.</summary>
    public static class UiAudio
    {
        public static System.Action<string> PlaySe;

        public static void Play(string id)
        {
            System.Action<string> p = PlaySe;
            if (p != null) p(id);
        }
    }
}
