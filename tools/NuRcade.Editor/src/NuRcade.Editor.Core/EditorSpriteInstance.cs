namespace NuRcade.Editor.Core;

public sealed class EditorSpriteDamageResponse
{
    public string Type { get; set; } = string.Empty;
    public double HitPoints { get; set; } = 45.0;
    public string? EffectSpriteSet { get; set; }
    public string? EffectAnimation { get; set; }
    public double EffectScaleCells { get; set; } = 1.5;
    public string? DestroyedSpriteSet { get; set; }
    public double DestroyedScaleCells { get; set; } = 0.55;
    public string? Sound { get; set; }
    public double RadiusCells { get; set; }
    public double Damage { get; set; }
}

public sealed class EditorSpriteInstance
{
    public string Name { get; set; } = string.Empty;
    public string SpriteSet { get; set; } = string.Empty;
    public double XCell { get; set; }
    public double YCell { get; set; }
    public double FacingDegrees { get; set; }
    public double ScaleCells { get; set; } = 1.0;
    public double VerticalOffsetCells { get; set; }
    public double CollisionRadiusCells { get; set; } = 0.2;
    public bool Visible { get; set; } = true;
    public bool PassThroughWalls { get; set; }
    public bool ChasePlayer { get; set; }
    public double SpeedCellsPerSecond { get; set; }
    public double DetectionRadiusCells { get; set; }
    public double PatrolRadiusCells { get; set; }
    public double EngagementHysteresisCells { get; set; } = 0.5;
    public bool PatrolCircuit { get; set; }
    public double StoppingDistanceCells { get; set; }
    public double MaxHealth { get; set; }
    public double Health { get; set; }
    public double AttackDamage { get; set; }
    public bool RangedAttack { get; set; }
    public double AttackRangeCells { get; set; }
    public double AttackCooldownSeconds { get; set; } = 1.0;
    public double AttackFovDegrees { get; set; } = 70.0;
    public int AttackBurstShots { get; set; } = 3;
    public double AttackBurstPauseSeconds { get; set; } = 1.2;
    public double PickupHealth { get; set; }
    public bool UnlocksMap { get; set; }
    public bool SavePoint { get; set; }
    public string? PickupWeapon { get; set; }
    public bool Explosive { get; set; }
    public double ExplosiveHitPoints { get; set; } = 45.0;
    public double ExplosionRadiusCells { get; set; }
    public double ExplosionDamage { get; set; }
    public double ExplosionScaleCells { get; set; } = 1.5;
    public string? ExplosionSpriteSet { get; set; }
    public string? DestroyedSpriteSet { get; set; }
    public double DestroyedScaleCells { get; set; } = 0.55;
    public EditorSpriteDamageResponse? DamageResponse { get; set; }
}
