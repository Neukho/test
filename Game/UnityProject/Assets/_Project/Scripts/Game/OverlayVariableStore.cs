using System;
using System.Collections.Generic;
using MorpheHouse.Dialogue;

namespace MorpheHouse.GameFlow
{
    /// <summary>
    /// GameFlagsVariableStore 위에 "임시 변수" 층을 얹는다.
    /// - 호스트가 넣는 임시 값($gift_name, $spot_name, $event_step ...)과 스크립트가 대입한 문자열은 이 층에만 저장된다(저장 파일에 들어가지 않음).
    /// - 숫자·불 대입은 내부 저장소(GameState.Flags)로 흘러간다.
    /// - 읽을 때는 임시 층이 우선이다.
    /// </summary>
    public sealed class OverlayVariableStore : IVariableStore
    {
        private readonly IVariableStore _inner;
        private readonly Dictionary<string, DialogueValue> _temps = new Dictionary<string, DialogueValue>();

        public OverlayVariableStore(IVariableStore inner)
        {
            if (inner == null) throw new ArgumentNullException("inner");
            _inner = inner;
        }

        public int TempCount { get { return _temps.Count; } }

        private static string Key(string name)
        {
            return name.StartsWith("$") ? name : "$" + name;
        }

        /// <summary>임시 값을 직접 넣는다(이름의 '$' 는 있어도 없어도 된다).</summary>
        public void SetTemp(string name, DialogueValue value)
        {
            _temps[Key(name)] = value;
        }

        public void ClearTemps()
        {
            _temps.Clear();
        }

        public bool TryGet(string name, out DialogueValue value)
        {
            if (_temps.TryGetValue(Key(name), out value)) return true;
            return _inner.TryGet(name, out value);
        }

        public void Set(string name, DialogueValue value)
        {
            string key = Key(name);
            if (value.IsString)
            {
                _temps[key] = value;
                return;
            }
            _temps.Remove(key);
            _inner.Set(name, value);
        }
    }
}
