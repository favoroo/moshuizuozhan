namespace P1.Weapons
{
    /// <summary>
    /// 主武器分类：决定 <see cref="WeaponController"/> 使用哪套策略驱动开火、蓄力与涂地。
    /// 数值全部来自 <see cref="WeaponConfigSO"/>，新增武器只需扩展枚举并补充对应策略。
    /// </summary>
    public enum WeaponKind
    {
        /// <summary>Spritzer：连发速射步枪，持续开火准星扩散。</summary>
        Shooter = 0,

        /// <summary>Swell Roller：推地碾压的滚筒，可挥击扇形墨滴。</summary>
        Roller = 1,

        /// <summary>Glint Charger：长按蓄力的贯穿狙击。</summary>
        Charger = 2,

        /// <summary>Twinfin Dualies：双手交替射击，双击跳跃可战术翻滚并架枪。</summary>
        Dualies = 3,

        /// <summary>Tidebucket Slosher：泼洒墨浪越过掩体，Volley 聚合防多段秒杀。</summary>
        Slosher = 4,

        /// <summary>Popper Blaster：慢速重弹直击秒杀，空中达到射程自动爆裂溅射。</summary>
        Blaster = 5,

        /// <summary>Gyre Splatling：按住蓄力旋转枪管，松开释放高密度弹幕暴风雨。</summary>
        Splatling = 6
    }
}
