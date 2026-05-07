using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace DeadZone.Actors
{
    [Serializable]
    public sealed class MinimapStageConfig
    {
        [BoxGroup("스테이지")]
        [SerializeField] private string sceneName;

        [BoxGroup("이미지")]
        [SerializeField] private Sprite minimapSprite;

        [BoxGroup("이미지")]
        [Tooltip("비워두면 Minimap Sprite를 전체 지도 이미지에도 사용합니다.")]
        [SerializeField] private Sprite worldMapSprite;

        [BoxGroup("월드 경계")]
        [SerializeField] private Vector2 worldMin = MapCoordinateUtility.FullMapWorldMin;

        [BoxGroup("월드 경계")]
        [SerializeField] private Vector2 worldMax = MapCoordinateUtility.FullMapWorldMax;

        [BoxGroup("정규화 보정")]
        [SerializeField] private Vector2 normalizedAnchor = new(0.9433962f, 0.6756757f);

        [BoxGroup("정규화 보정")]
        [SerializeField] private Vector2 normalizedScale = new(0.65f, 0.65f);

        [BoxGroup("정규화 보정")]
        [SerializeField] private Vector2 normalizedOffset = Vector2.zero;

        [BoxGroup("레이아웃")]
        [SerializeField] private Vector2 miniMapSize = new(300f, 300f);

        [BoxGroup("레이아웃")]
        [SerializeField] private Vector2 mapImageSize = new(1000f, 563f);

        public string SceneName => sceneName;
        public Sprite MinimapSprite => minimapSprite;
        public Sprite WorldMapSprite => worldMapSprite != null ? worldMapSprite : minimapSprite;
        public Vector2 WorldMin => worldMin;
        public Vector2 WorldMax => worldMax;
        public Vector2 NormalizedAnchor => normalizedAnchor;
        public Vector2 NormalizedScale => normalizedScale;
        public Vector2 NormalizedOffset => normalizedOffset;
        public Vector2 MiniMapSize => miniMapSize;
        public Vector2 MapImageSize => mapImageSize;
    }
}
