using System;
using System.Collections.Generic;

namespace MorpheHouse.Core
{
    /// <summary>모든 게임 이벤트가 구현하는 마커 인터페이스. 이벤트는 반드시 struct 로 만든다.</summary>
    public interface IGameEvent { }

    /// <summary>
    /// 모듈 간 통신용 타입 안전 이벤트 버스.
    /// - 이벤트 타입별 정적 채널(Channel&lt;T&gt;)을 사용해 Publish 시 할당이 없다.
    /// - 구독/해지는 copy-on-write 배열이므로 Publish 중 구독 변경이 안전하다.
    /// </summary>
    public static class EventBus
    {
        private static readonly List<Action> Clearers = new List<Action>();

        /// <summary>핸들러에서 예외가 발생했을 때 호출된다(Unity 쪽에서 Debug.LogException 연결).</summary>
        public static Action<Exception> OnHandlerException;

        private static class Channel<T> where T : struct, IGameEvent
        {
            public static Action<T>[] Handlers = new Action<T>[0];

            static Channel()
            {
                Clearers.Add(() => { Handlers = new Action<T>[0]; });
            }
        }

        /// <summary>이벤트를 구독한다.</summary>
        public static void Subscribe<T>(Action<T> handler) where T : struct, IGameEvent
        {
            if (handler == null) throw new ArgumentNullException("handler");
            Action<T>[] old = Channel<T>.Handlers;
            Action<T>[] next = new Action<T>[old.Length + 1];
            Array.Copy(old, next, old.Length);
            next[old.Length] = handler;
            Channel<T>.Handlers = next;
        }

        /// <summary>구독을 해지한다. 등록되지 않은 핸들러는 무시한다.</summary>
        public static void Unsubscribe<T>(Action<T> handler) where T : struct, IGameEvent
        {
            Action<T>[] old = Channel<T>.Handlers;
            int index = Array.IndexOf(old, handler);
            if (index < 0) return;
            Action<T>[] next = new Action<T>[old.Length - 1];
            Array.Copy(old, 0, next, 0, index);
            Array.Copy(old, index + 1, next, index, old.Length - index - 1);
            Channel<T>.Handlers = next;
        }

        /// <summary>이벤트를 발행한다. 핸들러 예외는 다른 핸들러 실행을 막지 않는다.</summary>
        public static void Publish<T>(T evt) where T : struct, IGameEvent
        {
            Action<T>[] handlers = Channel<T>.Handlers;
            for (int i = 0; i < handlers.Length; i++)
            {
                try
                {
                    handlers[i](evt);
                }
                catch (Exception ex)
                {
                    if (OnHandlerException != null) OnHandlerException(ex);
                    else throw;
                }
            }
        }

        /// <summary>모든 구독을 제거한다(테스트·씬 리셋용).</summary>
        public static void ClearAll()
        {
            for (int i = 0; i < Clearers.Count; i++) Clearers[i]();
        }
    }
}
