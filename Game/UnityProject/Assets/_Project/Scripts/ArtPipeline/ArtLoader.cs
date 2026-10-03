using System.Collections.Generic;
using MorpheHouse.Core;
using UnityEngine;

namespace MorpheHouse.ArtPipeline
{
    /// <summary>
    /// 스프라이트 로더. Resources 에 같은 파일명의 실제 일러스트가 있으면 그것을, 없으면 플레이스홀더를 즉석 생성한다.
    /// → 일러스트를 같은 파일명으로 교체하기만 하면 코드 수정 없이 반영된다.
    /// (Addressables 로 이전할 때는 이 클래스 안쪽만 바꾼다.)
    /// </summary>
    public static class ArtLoader
    {
        // ---- 캐시 상한(개수 / 추정 바이트). 근거와 메모리 추정은 docs/15_Performance_Guide.md §3.1 ----
        // 화면에 동시에 올라가는 최악: 스탠딩 3명×(몸+얼굴)=6장, 배경 2장(전환 중), CG 1장. 상한은 그 2배 안팎(touch on use 라서 화면에 쓰는 것이 항상 가장 최근).
        public const int StandingMaxCount = 12;
        public const int BackgroundMaxCount = 4;
        public const int CgMaxCount = 3;
        public const int IconMaxCount = 64;
        public const long StandingMaxBytes = 96L * 1024 * 1024;
        public const long BackgroundMaxBytes = 40L * 1024 * 1024;
        public const long CgMaxBytes = 28L * 1024 * 1024;
        public const long IconMaxBytes = 64L * 1024 * 1024;

        /// <summary>UnloadUnusedAssets 최소 간격(초, 실시간). 이 호출은 프레임 끊김을 만들 수 있어 화면 전환 때만, 이 간격으로만 부른다.</summary>
        public const float UnloadMinIntervalSec = 20f;

        /// <summary>캐시 항목: 스프라이트와 "우리가 만든 것인가"(Resources 에셋은 만든 것이 아니므로 파괴하지 않는다).</summary>
        private sealed class Slot
        {
            public Sprite Sprite;
            public Texture2D OwnedTexture;   // 플레이스홀더 텍스처(우리가 만듦). Resources 텍스처는 null.
            public bool OwnsSprite;          // Sprite.Create 로 만든 스프라이트(플레이스홀더 또는 원본 텍스처 구제용)
        }

        private static readonly LruCache<string, Slot> Standing = new LruCache<string, Slot>(StandingMaxCount, StandingMaxBytes, OnEvict);
        private static readonly LruCache<string, Slot> Backgrounds = new LruCache<string, Slot>(BackgroundMaxCount, BackgroundMaxBytes, OnEvict);
        private static readonly LruCache<string, Slot> Cgs = new LruCache<string, Slot>(CgMaxCount, CgMaxBytes, OnEvict);
        private static readonly LruCache<string, Slot> Icons = new LruCache<string, Slot>(IconMaxCount, IconMaxBytes, OnEvict);

        // 내보낸 항목의 파괴는 화면 전환 때까지 미룬다: 지금 보이는 Image 가 방금 밀려난 스프라이트를 들고 있어도 그 화면이 떠 있는 동안은 살아 있다.
        private static readonly List<Slot> Graveyard = new List<Slot>();
        private static float _lastUnload = -1000f;
        private static int _evictedSinceUnload;

        /// <summary>지금까지 내보낸 항목 수(진단용).</summary>
        public static int TotalEvictions { get; private set; }

        private static LruCache<string, Slot> CacheFor(ArtKind kind)
        {
            switch (kind)
            {
                case ArtKind.StandingBody:
                case ArtKind.StandingFace: return Standing;
                case ArtKind.Background: return Backgrounds;
                case ArtKind.EventCg:
                case ArtKind.CommonCg: return Cgs;
                default: return Icons;
            }
        }

        public static Sprite Get(ArtSpec spec)
        {
            LruCache<string, Slot> cache = CacheFor(spec.Kind);
            Slot slot;
            if (cache.TryGet(spec.ResPath, out slot) && slot.Sprite != null) return slot.Sprite;
            if (slot != null) cache.Remove(spec.ResPath); // 파괴된 항목(씬 정리 등)은 치운다.

            slot = new Slot();
            Sprite sprite = Resources.Load<Sprite>(spec.ResPath);
            if (sprite == null)
            {
                // 임포트 설정이 Sprite 가 아닌 원본 텍스처로 들어온 경우를 구제한다.
                Texture2D raw = Resources.Load<Texture2D>(spec.ResPath);
                if (raw != null)
                {
                    Debug.LogWarning("[ArtLoader] '" + spec.ResPath + "' 의 Texture Type 이 Sprite 가 아닙니다. 임시로 스프라이트를 만들어 사용합니다.");
                    sprite = Sprite.Create(raw, new Rect(0, 0, raw.width, raw.height), Pivot(spec.Kind), 100f);
                    slot.OwnsSprite = true; // 스프라이트만 우리 것. 텍스처(raw)는 Resources 에셋이라 파괴하지 않는다.
                }
            }
            if (sprite == null)
            {
                slot.OwnedTexture = MakePlaceholderTexture(spec);
                sprite = Sprite.Create(slot.OwnedTexture, new Rect(0, 0, spec.Width, spec.Height), Pivot(spec.Kind), 100f);
                slot.OwnsSprite = true;
            }
            slot.Sprite = sprite;
            cache.Put(spec.ResPath, slot, EstimateBytes(spec, slot));
            return sprite;
        }

        // 플레이스홀더는 RGBA32(4바이트/픽셀). 실제 에셋은 임포트 압축 형식을 알 수 없어 같은 식으로 보수적으로(과대) 추정한다 — 상한은 안전한 쪽으로 어긋난다.
        private static long EstimateBytes(ArtSpec spec, Slot slot)
        {
            Texture tex = slot.OwnedTexture;
            if (tex == null && slot.Sprite != null) tex = slot.Sprite.texture;
            int w = tex != null ? tex.width : spec.Width;
            int h = tex != null ? tex.height : spec.Height;
            return (long)w * h * 4L;
        }

        private static void OnEvict(string key, Slot slot)
        {
            TotalEvictions++;
            _evictedSinceUnload++;
            if (slot != null && slot.OwnsSprite) Graveyard.Add(slot);
        }

        /// <summary>
        /// 화면 전환 때 호출(AppContext.OnScreenChanged): 밀려난 플레이스홀더 텍스처·스프라이트를 파괴하고,
        /// 내보낸 것이 있으면(그리고 마지막 호출로부터 <see cref="UnloadMinIntervalSec"/> 이상 지났으면) Resources.UnloadUnusedAssets 로 Resources 에셋을 회수한다.
        /// 새 화면이 이미 자기 스프라이트를 받아 간 뒤(OnShow)에 불리므로, 지금 화면이 쓰는 스프라이트는 가장 최근 항목이라 안전하다.
        /// </summary>
        public static void OnScreenTransition()
        {
            FlushGraveyard();
            if (_evictedSinceUnload > 0 && Throttle.Elapsed(Time.realtimeSinceStartup, _lastUnload, UnloadMinIntervalSec))
            {
                _lastUnload = Time.realtimeSinceStartup;
                _evictedSinceUnload = 0;
                Resources.UnloadUnusedAssets();
            }
        }

        private static void FlushGraveyard()
        {
            for (int i = 0; i < Graveyard.Count; i++)
            {
                Slot s = Graveyard[i];
                if (s.Sprite != null) Object.Destroy(s.Sprite);
                if (s.OwnedTexture != null) Object.Destroy(s.OwnedTexture);
            }
            Graveyard.Clear();
        }

        public static Sprite StandingBody(string charId, string outfit)
        {
            return Get(ArtManifest.StandingBody(charId, outfit));
        }

        public static Sprite StandingFace(string charId, string outfit, string expression)
        {
            return Get(ArtManifest.StandingFace(charId, outfit, expression));
        }

        public static Sprite Cg(string charId, int number)
        {
            return Get(ArtManifest.Cg(charId, number));
        }

        /// <summary>공통 CG(예 HAREM_E1). key 는 대문자 정규화된 키. 파일이 없으면 플레이스홀더.</summary>
        public static Sprite CommonCg(string key)
        {
            return Get(ArtManifest.CommonCg(key));
        }

        public static Sprite Background(string place, string time)
        {
            return Get(ArtManifest.Background(place, time));
        }

        public static Sprite MenuIcon(string menuId)
        {
            return Get(ArtManifest.MenuIcon(menuId));
        }

        /// <summary>캐시 비우기(에디터에서 일러스트를 교체한 직후 확인용). 우리가 만든 플레이스홀더는 즉시 파괴한다.</summary>
        public static void ClearCache()
        {
            Standing.Clear();
            Backgrounds.Clear();
            Cgs.Clear();
            Icons.Clear();
            FlushGraveyard();
        }

        /// <summary>현재 캐시 항목 수 합(진단·테스트용).</summary>
        public static int CachedCount { get { return Standing.Count + Backgrounds.Count + Cgs.Count + Icons.Count; } }

        private static Vector2 Pivot(ArtKind kind)
        {
            return kind == ArtKind.StandingBody || kind == ArtKind.StandingFace ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 0.5f);
        }

        private static Texture2D MakePlaceholderTexture(ArtSpec spec)
        {
            Texture2D tex = new Texture2D(spec.Width, spec.Height, TextureFormat.RGBA32, false);
            tex.name = "PH_" + spec.ResPath;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixels32(PlaceholderPainter.Paint(spec));
            tex.Apply(false, false);
            return tex;
        }
    }
}
