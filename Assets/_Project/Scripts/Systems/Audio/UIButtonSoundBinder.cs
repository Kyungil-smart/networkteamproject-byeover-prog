using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

using DeadZone.Core;

namespace DeadZone.Systems.Audio
{
    // 씬 안의 모든 Unity UI Button에 클릭음을 자동으로 연결
    // 기존 UI 스크립트를 수정하지 않고 UI 클릭음을 붙이기 위한 1차 오디오 연결용 컴포넌트
    [DisallowMultipleComponent]
    public sealed class UIButtonSoundBinder : MonoBehaviour
    {
        [Header("====클릭음 설정====")]
        [Tooltip("버튼 클릭 시 재생할 UI 사운드 ID")]
        [SerializeField] private AudioCueId clickCueId = AudioCueId.UIButtonClick;

        [Tooltip("버튼 클릭음 볼륨 배율, AudioManager의 UI 볼륨과 개별 볼륨이 함께 적용")]
        [SerializeField, Range(0f, 2f)] private float volumeMultiplier = 1f;

        [Tooltip("비활성화된 버튼까지 미리 찾아 클릭음 리스너를 연결")]
        [SerializeField] private bool includeInactiveButtons = true;

        [Tooltip("씬 로드 직후 UI가 늦게 생성되는 경우를 위해 한 프레임 뒤 다시 바인딩")]
        [SerializeField] private bool rebindAfterOneFrame = true;

        private readonly Dictionary<Button, UnityAction> boundButtons = new();
        private Coroutine delayedBindRoutine;

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            BindSceneButtons();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;

            if (delayedBindRoutine != null)
            {
                StopCoroutine(delayedBindRoutine);
                delayedBindRoutine = null;
            }

            UnbindAll();
        }

        public void BindSceneButtons()
        {
            Button[] buttons = FindObjectsByType<Button>(
                includeInactiveButtons ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            for (int i = 0; i < buttons.Length; i++)
                BindButton(buttons[i]);
        }

        private void BindButton(Button button)
        {
            if (button == null || boundButtons.ContainsKey(button))
                return;

            UnityAction action = PublishClickSound;
            button.onClick.AddListener(action);
            boundButtons.Add(button, action);
        }

        private void UnbindAll()
        {
            foreach (KeyValuePair<Button, UnityAction> pair in boundButtons)
            {
                if (pair.Key != null)
                    pair.Key.onClick.RemoveListener(pair.Value);
            }

            boundButtons.Clear();
        }

        private void PublishClickSound()
        {
            EventBus.Publish(new AudioPlayRequestedEvent
            {
                cueId = clickCueId,
                position = Vector3.zero,
                use3D = false,
                volumeMultiplier = volumeMultiplier,
            });
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            UnbindAll();
            BindSceneButtons();

            if (rebindAfterOneFrame)
            {
                if (delayedBindRoutine != null)
                    StopCoroutine(delayedBindRoutine);

                delayedBindRoutine = StartCoroutine(BindAfterOneFrame());
            }
        }

        private IEnumerator BindAfterOneFrame()
        {
            yield return null;
            BindSceneButtons();
            delayedBindRoutine = null;
        }
    }
}
