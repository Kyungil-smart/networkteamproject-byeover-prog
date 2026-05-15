using UnityEngine;

namespace DeadZone.Systems.Audio
{
    [CreateAssetMenu(menuName = "DeadZone/Audio/Audio Library", fileName = "AudioLibrary")]
    public sealed class AudioLibrarySO : ScriptableObject
    {
        [Header("====무기 사운드====")]
        [Tooltip("AR 소총 발사 사운드 설정입니다.")]
        [SerializeField] private AudioCueData arFire = new();

        [Tooltip("AR 소총 장전 사운드 설정입니다.")]
        [SerializeField] private AudioCueData arReload = new();

        [Tooltip("HG 권총 발사 사운드 설정입니다.")]
        [SerializeField] private AudioCueData hgFire = new();

        [Tooltip("HG 권총 장전 사운드 설정입니다.")]
        [SerializeField] private AudioCueData hgReload = new();

        [Tooltip("SR 저격소총 발사 사운드 설정입니다.")]
        [SerializeField] private AudioCueData srDragFire = new();

        [Tooltip("SR 저격소총 장전 사운드 설정입니다.")]
        [SerializeField] private AudioCueData srDragReload = new();

        [Tooltip("SMG 발사 사운드 설정입니다.")]
        [SerializeField] private AudioCueData smgFire = new();

        [Tooltip("SMG 장전 사운드 설정입니다.")]
        [SerializeField] private AudioCueData smgReload = new();

        [Tooltip("샷건 발사 사운드 설정입니다.")]
        [SerializeField] private AudioCueData shotgunFire = new();

        [Tooltip("샷건 장전 사운드 설정입니다.")]
        [SerializeField] private AudioCueData shotgunReload = new();

        [Tooltip("일반 적 공격 사운드 설정입니다.")]
        [SerializeField] private AudioCueData enemyAttack = new();

        [Tooltip("보스 공격 사운드 설정입니다.")]
        [SerializeField] private AudioCueData bossAttack = new();

        [Header("====캐릭터 사운드====")]
        [Tooltip("플레이어 발걸음 사운드 설정입니다.")]
        [SerializeField] private AudioCueData playerFootstep = new();

        [Tooltip("적 발걸음 사운드 설정입니다.")]
        [SerializeField] private AudioCueData enemyFootstep = new();

        [Tooltip("적 발각음 사운드 설정입니다.")]
        [SerializeField] private AudioCueData enemyAlert = new();

        [Tooltip("플레이어 부상 사운드 설정입니다.")]
        [SerializeField] private AudioCueData playerInjured = new();

        [Tooltip("플레이어 기절 상태 진입 사운드 설정입니다.")]
        [SerializeField] private AudioCueData playerKnocked = new();

        [Header("====상호작용 사운드====")]
        [Tooltip("파밍 상자를 열 때 재생할 사운드 설정입니다.")]
        [SerializeField] private AudioCueData loot1 = new();

        [Tooltip("파밍 상자에서 아이템을 인벤토리로 옮길 때 재생할 사운드 설정입니다.")]
        [SerializeField] private AudioCueData loot2 = new();

        [Header("====UI 사운드====")]
        [Tooltip("UI 버튼 클릭 사운드 설정입니다.")]
        [SerializeField] private AudioCueData uiButtonClick = new();

        [Header("====BGM====")]
        [Tooltip("Title BGM 설정입니다.")]
        [SerializeField] private AudioCueData titleBgm = new();

        [Tooltip("Stage1 BGM 설정입니다.")]
        [SerializeField] private AudioCueData stage1Bgm = new();

        [Tooltip("Stage2 BGM 설정입니다.")]
        [SerializeField] private AudioCueData stage2Bgm = new();

        [Tooltip("Ending BGM 설정입니다.")]
        [SerializeField] private AudioCueData endingBgm = new();

        public AudioCueData[] Cues => new[]
        {
            arFire,
            arReload,
            hgFire,
            hgReload,
            srDragFire,
            srDragReload,
            smgFire,
            smgReload,
            shotgunFire,
            shotgunReload,
            enemyAttack,
            bossAttack,
            playerFootstep,
            enemyFootstep,
            enemyAlert,
            loot1,
            loot2,
            playerInjured,
            playerKnocked,
            uiButtonClick,
            titleBgm,
            stage1Bgm,
            stage2Bgm,
            endingBgm,
        };

        public bool TryGetCue(AudioCueId cueId, out AudioCueData cue)
        {
            cue = GetCue(cueId);
            return cue != null;
        }

        public AudioCueData GetCue(AudioCueId cueId)
        {
            return cueId switch
            {
                AudioCueId.ARFire => arFire,
                AudioCueId.ARReload => arReload,
                AudioCueId.HGFire => hgFire,
                AudioCueId.HGReload => hgReload,
                AudioCueId.SRDragFire => srDragFire,
                AudioCueId.SRDragReload => srDragReload,
                AudioCueId.SMGFire => smgFire,
                AudioCueId.SMGReload => smgReload,
                AudioCueId.ShotgunFire => shotgunFire,
                AudioCueId.ShotgunReload => shotgunReload,
                AudioCueId.EnemyAttack => enemyAttack,
                AudioCueId.BossAttack => bossAttack,
                AudioCueId.PlayerFootstep => playerFootstep,
                AudioCueId.EnemyFootstep => enemyFootstep,
                AudioCueId.EnemyAlert => enemyAlert,
                AudioCueId.Loot1 => loot1,
                AudioCueId.Loot2 => loot2,
                AudioCueId.PlayerInjured => playerInjured,
                AudioCueId.PlayerKnocked => playerKnocked,
                AudioCueId.UIButtonClick => uiButtonClick,
                AudioCueId.TitleBGM => titleBgm,
                AudioCueId.Stage1BGM => stage1Bgm,
                AudioCueId.Stage2BGM => stage2Bgm,
                AudioCueId.EndingBGM => endingBgm,
                _ => null,
            };
        }

        private void Reset()
        {
            ApplyFixedCueInfo();
        }

        private void OnValidate()
        {
            ApplyFixedCueInfo();
            ValidateCue(arFire);
            ValidateCue(arReload);
            ValidateCue(hgFire);
            ValidateCue(hgReload);
            ValidateCue(srDragFire);
            ValidateCue(srDragReload);
            ValidateCue(smgFire);
            ValidateCue(smgReload);
            ValidateCue(shotgunFire);
            ValidateCue(shotgunReload);
            ValidateCue(enemyAttack);
            ValidateCue(bossAttack);
            ValidateCue(playerFootstep);
            ValidateCue(enemyFootstep);
            ValidateCue(enemyAlert);
            ValidateCue(loot1);
            ValidateCue(loot2);
            ValidateCue(playerInjured);
            ValidateCue(playerKnocked);
            ValidateCue(uiButtonClick);
            ValidateCue(titleBgm);
            ValidateCue(stage1Bgm);
            ValidateCue(stage2Bgm);
            ValidateCue(endingBgm);
        }

        private void ApplyFixedCueInfo()
        {
            if (arFire == null) arFire = new AudioCueData();
            if (arReload == null) arReload = new AudioCueData();
            if (hgFire == null) hgFire = new AudioCueData();
            if (hgReload == null) hgReload = new AudioCueData();
            if (srDragFire == null) srDragFire = new AudioCueData();
            if (srDragReload == null) srDragReload = new AudioCueData();
            if (smgFire == null) smgFire = new AudioCueData();
            if (smgReload == null) smgReload = new AudioCueData();
            if (shotgunFire == null) shotgunFire = new AudioCueData();
            if (shotgunReload == null) shotgunReload = new AudioCueData();
            if (enemyAttack == null) enemyAttack = new AudioCueData();
            if (bossAttack == null) bossAttack = new AudioCueData();
            if (playerFootstep == null) playerFootstep = new AudioCueData();
            if (enemyFootstep == null) enemyFootstep = new AudioCueData();
            if (enemyAlert == null) enemyAlert = new AudioCueData();
            if (loot1 == null) loot1 = new AudioCueData();
            if (loot2 == null) loot2 = new AudioCueData();
            if (playerInjured == null) playerInjured = new AudioCueData();
            if (playerKnocked == null) playerKnocked = new AudioCueData();
            if (uiButtonClick == null) uiButtonClick = new AudioCueData();
            if (titleBgm == null) titleBgm = new AudioCueData();
            if (stage1Bgm == null) stage1Bgm = new AudioCueData();
            if (stage2Bgm == null) stage2Bgm = new AudioCueData();
            if (endingBgm == null) endingBgm = new AudioCueData();

            arFire.SetFixedInfo(AudioCueId.ARFire, "AR 소총 발사", AudioGroup.SFX);
            arReload.SetFixedInfo(AudioCueId.ARReload, "AR 소총 장전", AudioGroup.SFX);
            hgFire.SetFixedInfo(AudioCueId.HGFire, "HG 권총 발사", AudioGroup.SFX);
            hgReload.SetFixedInfo(AudioCueId.HGReload, "HG 권총 장전", AudioGroup.SFX);
            srDragFire.SetFixedInfo(AudioCueId.SRDragFire, "SR 저격소총 발사", AudioGroup.SFX);
            srDragReload.SetFixedInfo(AudioCueId.SRDragReload, "SR 저격소총 장전", AudioGroup.SFX);
            smgFire.SetFixedInfo(AudioCueId.SMGFire, "SMG 발사", AudioGroup.SFX);
            smgReload.SetFixedInfo(AudioCueId.SMGReload, "SMG 장전", AudioGroup.SFX);
            shotgunFire.SetFixedInfo(AudioCueId.ShotgunFire, "샷건 발사", AudioGroup.SFX);
            shotgunReload.SetFixedInfo(AudioCueId.ShotgunReload, "샷건 장전", AudioGroup.SFX);
            enemyAttack.SetFixedInfo(AudioCueId.EnemyAttack, "일반 적 공격", AudioGroup.SFX);
            bossAttack.SetFixedInfo(AudioCueId.BossAttack, "보스 공격", AudioGroup.SFX);
            playerFootstep.SetFixedInfo(AudioCueId.PlayerFootstep, "플레이어 발걸음", AudioGroup.SFX);
            enemyFootstep.SetFixedInfo(AudioCueId.EnemyFootstep, "적 발걸음", AudioGroup.SFX);
            enemyAlert.SetFixedInfo(AudioCueId.EnemyAlert, "적 발각음", AudioGroup.SFX);
            loot1.SetFixedInfo(AudioCueId.Loot1, "파밍 상자 열기", AudioGroup.SFX);
            loot2.SetFixedInfo(AudioCueId.Loot2, "파밍 아이템 획득", AudioGroup.SFX);
            playerInjured.SetFixedInfo(AudioCueId.PlayerInjured, "플레이어 부상", AudioGroup.SFX);
            playerKnocked.SetFixedInfo(AudioCueId.PlayerKnocked, "플레이어 기절", AudioGroup.SFX);
            uiButtonClick.SetFixedInfo(AudioCueId.UIButtonClick, "UI 버튼 클릭", AudioGroup.UI);
            titleBgm.SetFixedInfo(AudioCueId.TitleBGM, "Title BGM", AudioGroup.BGM);
            stage1Bgm.SetFixedInfo(AudioCueId.Stage1BGM, "Stage1 BGM", AudioGroup.BGM);
            stage2Bgm.SetFixedInfo(AudioCueId.Stage2BGM, "Stage2 BGM", AudioGroup.BGM);
            endingBgm.SetFixedInfo(AudioCueId.EndingBGM, "Ending BGM", AudioGroup.BGM);
        }

        private static void ValidateCue(AudioCueData cue)
        {
            if (cue == null)
                return;

            cue.maxPitch = Mathf.Max(cue.minPitch, cue.maxPitch);
            cue.maxDistance = Mathf.Max(0.01f, cue.maxDistance);
        }
    }
}
