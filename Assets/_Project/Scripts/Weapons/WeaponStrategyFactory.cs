using UnityEngine;

namespace P1.Weapons
{
    /// <summary>按 <see cref="WeaponKind"/> 构造对应策略。新增武器类型时只在这里补一个分支。</summary>
    public static class WeaponStrategyFactory
    {
        public static MainWeaponStrategy Create(WeaponConfigSO config)
        {
            WeaponKind kind = config != null ? config.weaponKind : WeaponKind.Shooter;
            switch (kind)
            {
                case WeaponKind.Roller:
                    return new RollerWeaponStrategy(config);
                case WeaponKind.Charger:
                    return new ChargerWeaponStrategy(config);
                case WeaponKind.Dualies:
                    return new DualiesWeaponStrategy(config);
                case WeaponKind.Slosher:
                    return new SlosherWeaponStrategy(config);
                case WeaponKind.Blaster:
                    return new BlasterWeaponStrategy(config);
                case WeaponKind.Splatling:
                    return new SplatlingWeaponStrategy(config);
                default:
                    return new ShooterWeaponStrategy(config);
            }
        }

        public static MainWeaponStrategy Create(WeaponKind kind, WeaponConfigSO config)
        {
            switch (kind)
            {
                case WeaponKind.Roller:
                    return new RollerWeaponStrategy(config);
                case WeaponKind.Charger:
                    return new ChargerWeaponStrategy(config);
                case WeaponKind.Dualies:
                    return new DualiesWeaponStrategy(config);
                case WeaponKind.Slosher:
                    return new SlosherWeaponStrategy(config);
                case WeaponKind.Blaster:
                    return new BlasterWeaponStrategy(config);
                case WeaponKind.Splatling:
                    return new SplatlingWeaponStrategy(config);
                default:
                    return new ShooterWeaponStrategy(config);
            }
        }
    }
}
