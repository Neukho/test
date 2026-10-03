using System;

namespace MorpheHouse.GameFlow
{
    /// <summary>스탠딩 한 칸의 현재 상태.</summary>
    public sealed class StandingSlot
    {
        public string CharId;
        public string Expression;
        public string Outfit;
        internal int Stamp;
    }

    /// <summary>
    /// VN 무대의 스탠딩 배치(최대 2명) 규칙. 0 = 왼쪽, 1 = 오른쪽.
    /// 이미 나온 인물은 같은 칸에서 표정만 바뀌고, 새 인물은 빈 칸(왼쪽 우선), 꽉 차면 가장 오래전에 갱신된 칸을 교체한다.
    /// </summary>
    public sealed class VnStandingState
    {
        public const int MaxSlots = 2;

        private readonly StandingSlot[] _slots = new StandingSlot[MaxSlots];
        private int _clock;

        public int Count
        {
            get
            {
                int n = 0;
                for (int i = 0; i < MaxSlots; i++) if (_slots[i] != null) n++;
                return n;
            }
        }

        public StandingSlot At(int slot)
        {
            return slot >= 0 && slot < MaxSlots ? _slots[slot] : null;
        }

        public int SlotOf(string charId)
        {
            for (int i = 0; i < MaxSlots; i++)
                if (_slots[i] != null && _slots[i].CharId == charId) return i;
            return -1;
        }

        /// <summary>인물을 보이게 한다. 칸 번호를 돌려주고, 다른 인물을 밀어냈다면 evicted 에 그 id.</summary>
        public int Show(string charId, string expression, string outfit, out string evicted)
        {
            evicted = null;
            if (string.IsNullOrEmpty(charId)) throw new ArgumentException("charId", "charId");
            int slot = SlotOf(charId);
            if (slot < 0)
            {
                for (int i = 0; i < MaxSlots; i++)
                {
                    if (_slots[i] == null) { slot = i; break; }
                }
                if (slot < 0)
                {
                    slot = 0;
                    for (int i = 1; i < MaxSlots; i++)
                        if (_slots[i].Stamp < _slots[slot].Stamp) slot = i;
                    evicted = _slots[slot].CharId;
                }
                _slots[slot] = new StandingSlot();
                _slots[slot].CharId = charId;
            }
            _clock++;
            _slots[slot].Expression = expression;
            _slots[slot].Outfit = outfit;
            _slots[slot].Stamp = _clock;
            return slot;
        }

        /// <summary>인물을 내린다. 보이지 않던 인물이면 -1.</summary>
        public int Hide(string charId)
        {
            int slot = SlotOf(charId);
            if (slot >= 0) _slots[slot] = null;
            return slot;
        }

        public void Clear()
        {
            for (int i = 0; i < MaxSlots; i++) _slots[i] = null;
        }
    }
}
