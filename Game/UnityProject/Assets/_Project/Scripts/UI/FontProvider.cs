using System;
using TMPro;
using UnityEngine;

namespace MorpheHouse.UI
{
    /// <summary>
    /// 한글을 표시할 TMP 폰트를 제공한다.
    /// 우선순위: ① Resources/Fonts/MainFont (번들 폰트 TMP_FontAsset) ② OS 설치 한글 폰트로 만든 동적 폰트 ③ TMP 기본 폰트.
    /// 출시 전에는 ①(예: Noto Sans KR, SIL OFL)로 고정해 플랫폼별 차이를 없앤다.
    /// </summary>
    public static class FontProvider
    {
        private static readonly string[] OsCandidates =
        {
            "Malgun Gothic", "맑은 고딕", "Noto Sans CJK KR", "Noto Sans KR", "NanumGothic",
            "Apple SD Gothic Neo", "AppleGothic", "Arial Unicode MS"
        };

        private static TMP_FontAsset _asset;
        private static bool _resolved;

        public static TMP_FontAsset Asset
        {
            get
            {
                if (!_resolved)
                {
                    _resolved = true;
                    _asset = Resolve();
                }
                return _asset;
            }
        }

        private static TMP_FontAsset Resolve()
        {
            TMP_FontAsset bundled = Resources.Load<TMP_FontAsset>("Fonts/MainFont");
            if (bundled != null) return bundled;

            string[] installed = Font.GetOSInstalledFontNames();
            for (int i = 0; i < OsCandidates.Length; i++)
            {
                if (Array.IndexOf(installed, OsCandidates[i]) < 0) continue;
                Font os = Font.CreateDynamicFontFromOSFont(OsCandidates[i], 32);
                // 기본 설정(동적 아틀라스, 다중 아틀라스 허용)의 TMP 폰트 에셋을 OS 폰트로부터 만든다.
                TMP_FontAsset dynamic = TMP_FontAsset.CreateFontAsset(os);
                if (dynamic != null)
                {
                    dynamic.name = "OS_" + OsCandidates[i];
                    return dynamic;
                }
            }

            Debug.LogWarning("[FontProvider] 한글 폰트를 찾지 못했습니다. Resources/Fonts/MainFont 에 TMP_FontAsset 을 넣어 주세요.");
            return TMP_Settings.defaultFontAsset;
        }
    }
}
