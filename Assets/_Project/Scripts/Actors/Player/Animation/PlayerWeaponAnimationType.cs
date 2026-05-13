namespace DeadZone.Actors
{
    /// <summary>
    /// Player Animator의 WeaponType Int 파라미터와 1:1로 대응되는 무기 애니메이션 타입이다.
    /// Animator Controller에서는 이 값으로 비무장/소총형/권총/근접 자세를 구분한다.
    /// </summary>
    public enum PlayerWeaponAnimationType
    {
        Unarmed = 0,
        RifleLike = 1,
        Handgun = 2,
        Melee = 3
    }
}