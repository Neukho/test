using UnityEngine;

namespace MorpheHouse.Save
{
    /// <summary>Unity JsonUtility 기반 직렬화. 알 수 없는 필드는 무시되고, 누락 필드는 기본값을 쓴다.</summary>
    public sealed class JsonUtilitySerializer : ISerializer
    {
        public string ToJson(SaveData data)
        {
            return JsonUtility.ToJson(data, false);
        }

        public SaveData FromJson(string json)
        {
            return JsonUtility.FromJson<SaveData>(json);
        }
    }
}
