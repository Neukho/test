using MorpheHouse.Data;
using UnityEngine;

namespace MorpheHouse.Boot
{
    /// <summary>Resources/Balance/{name}.csv 를 TextAsset 으로 읽는다. 플랫폼(Android 포함)에서 동일하게 동작한다.</summary>
    public sealed class ResourcesTextSource : ITextSource
    {
        private readonly string _folder;

        public ResourcesTextSource(string folder)
        {
            _folder = folder;
        }

        public string Load(string name)
        {
            TextAsset asset = Resources.Load<TextAsset>(_folder + "/" + name);
            return asset != null ? asset.text : null;
        }
    }
}
