using System;
using System.Collections.Generic;
using UnityEngine;


namespace DeadZone.Core
{
    /// <summary>
    /// 타입 안전 제네릭 Pub/Sub 이벤트 버스.
    /// IGameEvent를 구현한 struct 이벤트를 사용하여 boxing/GC 부담을 제거한다.
    /// </summary>
    /// <remarks>
    /// 중요: Subscribe는 반드시 Unsubscribe와 쌍으로 호출해야 한다 (OnDisable / OnNetworkDespawn에서).
    /// 명시적 해제 없이 람다 캡처를 사용하는 것이 메모리 누수의 가장 큰 원인이다.
    /// </remarks>
    public static class EventBus
    {
        private static readonly Dictionary<Type, Delegate> handlers = new();

        /// <summary>T 타입 이벤트를 구독한다.</summary>
        public static void Subscribe<T>(Action<T> handler) where T : struct, IGameEvent
        {
            if (handler == null) return;
            var type = typeof(T);
            if (handlers.TryGetValue(type, out var existing))
            {
                handlers[type] = Delegate.Combine(existing, handler);
            }
            else
            {
                handlers[type] = handler;
            }
        }

        /// <summary>T 타입 이벤트 구독을 해제한다.</summary>
        public static void Unsubscribe<T>(Action<T> handler) where T : struct, IGameEvent
        {
            if (handler == null) return;
            var type = typeof(T);
            if (!handlers.TryGetValue(type, out var existing)) return;

            var newDelegate = Delegate.Remove(existing, handler);
            if (newDelegate == null)
            {
                handlers.Remove(type);
            }
            else
            {
                handlers[type] = newDelegate;
            }
        }

        /// <summary>이벤트를 발행한다. 현재 모든 구독자가 동기적으로 수신한다.</summary>
        public static void Publish<T>(T evt) where T : struct, IGameEvent
        {
            if (!handlers.TryGetValue(typeof(T), out var existing)) return;
            if (existing is Action<T> action)
            {
                try
                {
                    action.Invoke(evt);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[EventBus] {typeof(T).Name} 핸들러에서 예외 발생: {e}");
                }
            }
        }

        /// <summary>모든 구독을 해제한다. 씬 전환이나 앱 종료 시 호출.</summary>
        public static void Clear()
        {
            handlers.Clear();
        }

        /// <summary>현재 구독된 서로 다른 이벤트 타입의 개수 (디버그용).</summary>
        public static int SubscriberTypeCount => handlers.Count;
    }
}
