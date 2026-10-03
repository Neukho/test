using MorpheHouse.ArtPipeline;
using UnityEngine;
using UnityEngine.UI;

namespace MorpheHouse.UI
{
    /// <summary>
    /// 스탠딩 일러스트 표시: 몸(Body) + 얼굴(Face) 두 장을 겹쳐 표정을 교체한다.
    /// 최종 일러스트도 같은 캔버스 크기·앵커로 제작하므로 이 컴포넌트는 그대로 쓴다.
    /// </summary>
    public sealed class StandingView
    {
        private readonly Image _body;
        private readonly Image _face;
        private readonly string _charId;
        private string _outfit;

        public RectTransform Root { get; private set; }

        public StandingView(Transform parent, string charId, string outfit, float height)
        {
            _charId = charId;
            _outfit = outfit;
            Root = UiKit.NewRect("Standing_" + charId, parent);
            float width = height * ArtManifest.StandingWidth / ArtManifest.StandingHeight;
            UiKit.Place(Root, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(width, height), Vector2.zero);

            _body = Layer("Body");
            _face = Layer("Face");
            Refresh("Neutral");
        }

        private Image Layer(string name)
        {
            Image img = UiKit.NewImage(name, Root, Color.white);
            UiKit.Stretch(img.rectTransform);
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>몸·얼굴 레이어에 색을 곱한다(말하지 않는 인물을 어둡게 할 때).</summary>
        public void SetTint(Color color)
        {
            _body.color = color;
            _face.color = color;
        }

        public void SetExpression(string expression)
        {
            Refresh(expression);
        }

        public void SetOutfit(string outfit, string expression)
        {
            _outfit = outfit;
            Refresh(expression);
        }

        private void Refresh(string expression)
        {
            _body.sprite = ArtLoader.StandingBody(_charId, _outfit);
            _face.sprite = ArtLoader.StandingFace(_charId, _outfit, expression);
        }
    }
}
