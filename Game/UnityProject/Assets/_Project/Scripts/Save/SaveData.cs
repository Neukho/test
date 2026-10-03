using System;
using System.Collections.Generic;

namespace MorpheHouse.Save
{
    /// <summary>한 인물의 저장 데이터. JsonUtility 가 Dictionary 를 지원하지 않아 리스트로 저장한다.</summary>
    [Serializable]
    public sealed class CharSave
    {
        public string Id = string.Empty;
        public float Aff;
        public float Trust;
        public int EventsDone;
        public int LastGiftDay = -99;
        public int LastDateDay = -99;
        public int LastContactDay;
    }

    [Serializable]
    public sealed class FlagSave
    {
        public string Key = string.Empty;
        public int Value;
    }

    /// <summary>세이브 파일 스키마. 서명 대상(SaveIntegrity.SignedFields) 필드를 바꿀 때만 Version 을 올리고 SaveMigrations 에 단계를 추가한다. 서명 목록에 없는 선택 필드는 버전 없이 추가할 수 있으나 변조 검출 대상이 아니다.</summary>
    [Serializable]
    public sealed class SaveData
    {
        public const int CurrentVersion = 2;

        public int Version = CurrentVersion;
        public int Slot;
        public string SavedAtUtc = string.Empty;
        public string AppVersion = string.Empty;

        public string PlayerName = string.Empty;
        public string Gender = "neutral";
        /// <summary>2단계 추가 필드. 이전 저장 파일에는 없으므로 기본값(human/1)이 유지된다(v1 파일은 레거시 서명 경로로 검증된 뒤 SaveMigrations 로 v2 가 된다).</summary>
        public string PlayerSpecies = "human";
        public int PlayerAppearance = 1;
        public int Difficulty = 1;

        public int Chapter = 1;
        public int Day = 1;
        public int DayInChapter = 1;

        public float Gold;
        public float Reputation;
        public float Fatigue;
        public int ShopLevel = 1;
        public float SatisfactionBonus;

        public string LockedRoute = string.Empty;

        /// <summary>
        /// 공동(다자) 루트 구성원 목록(3단계 추가, 1~3명, 확정 순서). 이전 저장에는 없으므로 빈 목록이면 LockedRoute 만 쓴다(v1 파일은 레거시 서명 경로로 검증된 뒤 SaveMigrations 로 v2 가 된다).
        /// 비어 있지 않으면 첫 항목이 LockedRoute 와 같아야 한다.
        /// </summary>
        public List<string> LockedGroup = new List<string>();

        public double PlayTimeSec;

        public List<CharSave> Chars = new List<CharSave>();
        public List<FlagSave> Flags = new List<FlagSave>();

        /// <summary>방문한 대화 노드 목록(2단계 추가, 첫 방문 순서). 이전 저장에는 없으면 빈 목록.</summary>
        public List<string> VisitedNodes = new List<string>();

        /// <summary>무결성 해시(HMAC-SHA256, 16진수). v2+: 버전별 정규 페이로드(SaveIntegrity.CanonicalPayload)의 서명이며 서명 대상이 아니다. v1: 이 필드를 비운 재직렬화 텍스트의 서명(레거시).</summary>
        public string Hash = string.Empty;
    }
}
