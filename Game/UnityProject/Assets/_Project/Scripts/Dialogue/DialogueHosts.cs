using System;
using System.Collections.Generic;

namespace MorpheHouse.Dialogue
{
    /// <summary>람다로 함수·커맨드를 등록하는 범용 호스트. 함수 제공자(PronounResolver 등)도 붙일 수 있다.</summary>
    public sealed class SimpleDialogueHost : IDialogueHost
    {
        private readonly Dictionary<string, Func<DialogueValue[], DialogueValue>> _functions =
            new Dictionary<string, Func<DialogueValue[], DialogueValue>>();
        private readonly Dictionary<string, Action<string[]>> _commands = new Dictionary<string, Action<string[]>>();
        private readonly List<IDialogueFunctionProvider> _providers = new List<IDialogueFunctionProvider>();

        public void RegisterFunction(string name, Func<DialogueValue[], DialogueValue> fn)
        {
            _functions[name] = fn;
        }

        /// <summary>등록된 커맨드는 즉시 실행되고 러너에는 노출되지 않는다.</summary>
        public void RegisterCommand(string name, Action<string[]> handler)
        {
            _commands[name] = handler;
        }

        public void AddProvider(IDialogueFunctionProvider provider)
        {
            _providers.Add(provider);
        }

        public bool TryCallFunction(string name, DialogueValue[] args, out DialogueValue result)
        {
            Func<DialogueValue[], DialogueValue> fn;
            if (_functions.TryGetValue(name, out fn))
            {
                result = fn(args);
                return true;
            }
            for (int i = 0; i < _providers.Count; i++)
            {
                if (_providers[i].TryCall(name, args, out result)) return true;
            }
            result = default(DialogueValue);
            return false;
        }

        public bool TryHandleCommand(string name, string[] args)
        {
            Action<string[]> h;
            if (!_commands.TryGetValue(name, out h)) return false;
            h(args);
            return true;
        }
    }

    /// <summary>아무것도 처리하지 않는 호스트(host 인자 생략 시).</summary>
    public sealed class NullDialogueHost : IDialogueHost
    {
        public bool TryCallFunction(string name, DialogueValue[] args, out DialogueValue result)
        {
            result = default(DialogueValue);
            return false;
        }

        public bool TryHandleCommand(string name, string[] args)
        {
            return false;
        }
    }
}
