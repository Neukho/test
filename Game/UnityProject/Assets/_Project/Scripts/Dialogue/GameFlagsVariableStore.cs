using System;
using MorpheHouse.Data;

namespace MorpheHouse.Dialogue
{
    /// <summary>
    /// GameState 위의 변수 저장소. `$name` 은 GameState.Flags["name"](int) 에 대응한다.
    /// 불은 1/0, 숫자는 반올림한 정수로 저장한다(문자열 저장은 미지원 → 예외).
    /// 읽기 전용 가상 변수: $gold $reputation $fatigue $day $chapter $aff_{캐릭터} $trust_{캐릭터}
    /// (플래그에 같은 키가 있으면 플래그가 우선). 이 값을 바꾸는 것은 커맨드(aff/trust)로만 한다.
    /// </summary>
    public sealed class GameFlagsVariableStore : IVariableStore
    {
        private readonly GameState _state;

        public GameFlagsVariableStore(GameState state)
        {
            if (state == null) throw new ArgumentNullException("state");
            _state = state;
        }

        /// <summary>'$' 접두사를 뗀 플래그 키.</summary>
        public static string KeyOf(string name)
        {
            return name.StartsWith("$") ? name.Substring(1) : name;
        }

        public bool TryGet(string name, out DialogueValue value)
        {
            string key = KeyOf(name);
            int flag;
            if (_state.Flags.TryGetValue(key, out flag))
            {
                value = DialogueValue.FromNumber(flag);
                return true;
            }
            return TryGetBuiltIn(key, out value);
        }

        private bool TryGetBuiltIn(string key, out DialogueValue value)
        {
            switch (key)
            {
                case "gold": value = DialogueValue.FromNumber(_state.Gold); return true;
                case "reputation": value = DialogueValue.FromNumber(_state.Reputation); return true;
                case "fatigue": value = DialogueValue.FromNumber(_state.Fatigue); return true;
                case "day": value = DialogueValue.FromNumber(_state.Day); return true;
                case "chapter": value = DialogueValue.FromNumber(_state.Chapter); return true;
            }
            CharState c;
            if (key.StartsWith("aff_") && _state.Chars.TryGetValue(key.Substring(4), out c))
            {
                value = DialogueValue.FromNumber(c.Aff);
                return true;
            }
            if (key.StartsWith("trust_") && _state.Chars.TryGetValue(key.Substring(6), out c))
            {
                value = DialogueValue.FromNumber(c.Trust);
                return true;
            }
            value = default(DialogueValue);
            return false;
        }

        public void Set(string name, DialogueValue value)
        {
            string key = KeyOf(name);
            if (key.Length == 0) throw new ArgumentException("빈 변수 이름", "name");
            DialogueValue ignored;
            if (!_state.Flags.ContainsKey(key) && TryGetBuiltIn(key, out ignored))
                throw new InvalidOperationException("'" + name + "' 는 읽기 전용 가상 변수입니다. 커맨드(aff/trust 등)를 사용하세요.");
            if (value.IsString)
                throw new InvalidOperationException("GameFlags 에는 문자열을 저장할 수 없습니다: " + name);
            _state.Flags[key] = (int)Math.Round(value.AsNumber);
        }
    }
}
