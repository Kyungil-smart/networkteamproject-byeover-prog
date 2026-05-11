using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeadZone.Core
{
    /// <summary>
    /// 신규 시작 또는 파산신청 후 지급할 기본 보급품 구성을 정의합니다.
    /// </summary>
    [CreateAssetMenu(menuName = "DeadZone/Data/Starter Pack Config", fileName = "StarterPack_New")]
    public sealed class StarterPackConfigSO : ScriptableObject
    {
        [Header("시작 재화")]
        [Tooltip("파산신청 후 보유하게 될 기본 소지금입니다.")]
        [SerializeField] private int startingCredits = 50000;

        [Header("지급 아이템")]
        [Tooltip("파산신청 후 보관함에 순서대로 지급할 아이템 목록입니다.")]
        [SerializeField] private List<StarterPackEntry> entries = new List<StarterPackEntry>();

        /// <summary>
        /// 파산신청 후 적용할 기본 소지금입니다.
        /// </summary>
        public int StartingCredits => Mathf.Max(0, startingCredits);

        /// <summary>
        /// 파산신청 후 보관함에 지급할 아이템 목록입니다.
        /// </summary>
        public IReadOnlyList<StarterPackEntry> Entries => entries;
    }

    /// <summary>
    /// 스타터팩의 단일 아이템 지급 항목입니다.
    /// </summary>
    [Serializable]
    public sealed class StarterPackEntry
    {
        [Header("아이템")]
        [Tooltip("지급할 ItemDataSO입니다.")]
        [SerializeField] private ItemDataSO item;

        [Tooltip("지급할 수량입니다. 스택 가능한 아이템은 최대 스택 크기에 맞춰 자동 분할됩니다.")]
        [Min(1)]
        [SerializeField] private int amount = 1;

        [Header("상태")]
        [Tooltip("내구도가 있는 장비에 적용할 내구도 비율입니다. 1이면 최대 내구도입니다.")]
        [Range(0f, 1f)]
        [SerializeField] private float durabilityRatio = 1f;

        [Tooltip("무기 지급 시 저장할 현재 탄창 수입니다. 현재 장비 탄창 복원 UI가 없으면 0으로 두면 됩니다.")]
        [Min(0)]
        [SerializeField] private int currentAmmo;

        /// <summary>
        /// 지급할 아이템 데이터입니다.
        /// </summary>
        public ItemDataSO Item => item;

        /// <summary>
        /// 지급할 총 수량입니다.
        /// </summary>
        public int Amount => Mathf.Max(1, amount);

        /// <summary>
        /// 장비 아이템에 적용할 내구도 비율입니다.
        /// </summary>
        public float DurabilityRatio => Mathf.Clamp01(durabilityRatio);

        /// <summary>
        /// 무기 아이템에 저장할 현재 탄창 수입니다.
        /// </summary>
        public int CurrentAmmo => Mathf.Max(0, currentAmmo);
    }
}
