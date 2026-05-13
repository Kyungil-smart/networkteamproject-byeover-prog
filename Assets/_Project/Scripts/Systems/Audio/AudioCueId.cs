namespace DeadZone.Systems.Audio
{
    // AudioManager가 재생할 사운드 식별자
    // 새 사운드를 추가할 때는 이 enum에 ID를 먼저 추가하고 AudioLibrarySO에 클립을 연결
    public enum AudioCueId
    {
        None = 0,

        ARFire = 10,
        ARReload = 11,
        HGFire = 20,
        HGReload = 21,
        SRDragFire = 30,
        SRDragReload = 31,
        SMGFire = 40,
        SMGReload = 41,

        PlayerFootstep = 100,
        EnemyFootstep = 101,
        EnemyAlert = 102,

        Loot1 = 200,
        Loot2 = 201,
        PlayerInjured = 202,

        UIButtonClick = 300,

        TitleBGM = 400,
        Stage1BGM = 401,
        Stage2BGM = 402,
        EndingBGM = 403,
    }
}
