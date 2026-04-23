using System;
using System.Collections.Generic;
using UnityEngine;


namespace DeadZone.Core
{
    /// <summary>
    /// 경량 서비스 레지스트리. 각 매니저는 OnNetworkSpawn / Awake에서 자신을 등록하고
    /// OnNetworkDespawn / OnDestroy에서 해제한다.
    /// </summary>
    /// <remarks>
    /// 호출자 책임: Get&lt;T&gt;()의 반환값은 항상 null 체크할 것.
    /// 서비스가 아직 등록되지 않은 타이밍이 있다 (예: 씬 로드 직후).
    /// </remarks>
    public static class ServiceLocator
    {
        private static readonly Dictionary<Type, object> services = new();

        public static void Register<T>(T service) where T : class
        {
            if (service == null)
            {
                Debug.LogError($"[ServiceLocator] {typeof(T).Name} 등록 시도했으나 null이었음");
                return;
            }

            var type = typeof(T);
            if (services.ContainsKey(type))
            {
                Debug.LogWarning($"[ServiceLocator] 기존 서비스 덮어쓰기: {type.Name}");
            }
            services[type] = service;
        }

        public static T Get<T>() where T : class
        {
            return services.TryGetValue(typeof(T), out var s) ? s as T : null;
        }

        public static bool TryGet<T>(out T service) where T : class
        {
            if (services.TryGetValue(typeof(T), out var s) && s is T cast)
            {
                service = cast;
                return true;
            }
            service = null;
            return false;
        }

        public static void Unregister<T>() where T : class
        {
            services.Remove(typeof(T));
        }

        /// <summary>모든 서비스를 제거한다. 앱 종료 시에만 호출.</summary>
        public static void Clear()
        {
            services.Clear();
        }

        public static int RegisteredCount => services.Count;
    }
}
