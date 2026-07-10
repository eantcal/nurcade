using System.ComponentModel;
using System.Runtime.CompilerServices;
using WinRaycastEditor.Core;

namespace WinRaycastEditor;

public sealed class SpriteInstanceViewModel : INotifyPropertyChanged
{
    public SpriteInstanceViewModel(EditorSpriteInstance sprite)
    {
        Sprite = sprite;
    }

    public EditorSpriteInstance Sprite { get; }
    public string Name
    {
        get => Sprite.Name;
        set
        {
            if (Sprite.Name == value) {
                return;
            }

            Sprite.Name = value;
            NotifyChanged();
        }
    }

    public string SpriteSet => Sprite.SpriteSet;

    public bool IsKey => Sprite.SpriteSet.StartsWith("item_key_", StringComparison.OrdinalIgnoreCase)
        || Sprite.SpriteSet.Contains("_key_", StringComparison.OrdinalIgnoreCase)
        || Sprite.SpriteSet.EndsWith("_key", StringComparison.OrdinalIgnoreCase);
    public IReadOnlyList<string> KeyColorOptions { get; } = ["red", "green", "blue"];
    public string KeyColor
    {
        get
        {
            foreach (var color in KeyColorOptions) {
                if (Sprite.SpriteSet.Contains(color, StringComparison.OrdinalIgnoreCase)) {
                    return color;
                }
            }

            return "red";
        }
        set
        {
            if (!IsKey || string.IsNullOrWhiteSpace(value)
                || string.Equals(KeyColor, value, StringComparison.OrdinalIgnoreCase)) {
                return;
            }

            var current = KeyColor;
            Sprite.SpriteSet = Sprite.SpriteSet.Replace(
                current,
                value.Trim().ToLowerInvariant(),
                StringComparison.OrdinalIgnoreCase);
            NotifyChanged(nameof(SpriteSet));
            NotifyChanged(nameof(KeyColor));
        }
    }

    public bool IsItem => EditorSpriteClassifier.IsItemSpriteSet(Sprite.SpriteSet);
    public bool IsActor => !IsItem;
    public string CategoryLabel => IsItem ? "Item" : "Enemy";
    public double XCell
    {
        get => Sprite.XCell;
        set
        {
            if (Math.Abs(Sprite.XCell - value) < 0.001) {
                return;
            }

            Sprite.XCell = value;
            NotifyChanged();
        }
    }

    public double YCell
    {
        get => Sprite.YCell;
        set
        {
            if (Math.Abs(Sprite.YCell - value) < 0.001) {
                return;
            }

            Sprite.YCell = value;
            NotifyChanged();
        }
    }

    public double FacingDegrees
    {
        get => Sprite.FacingDegrees;
        set
        {
            if (Math.Abs(Sprite.FacingDegrees - value) < 0.001) {
                return;
            }

            Sprite.FacingDegrees = value;
            NotifyChanged();
        }
    }

    public double ScaleCells
    {
        get => Sprite.ScaleCells;
        set
        {
            if (Math.Abs(Sprite.ScaleCells - value) < 0.001) {
                return;
            }

            Sprite.ScaleCells = value;
            NotifyChanged();
        }
    }

    public double CollisionRadiusCells
    {
        get => Sprite.CollisionRadiusCells;
        set
        {
            if (Math.Abs(Sprite.CollisionRadiusCells - value) < 0.001) {
                return;
            }

            Sprite.CollisionRadiusCells = value;
            NotifyChanged();
        }
    }

    public bool SavePoint
    {
        get => Sprite.SavePoint;
        set
        {
            if (Sprite.SavePoint == value) {
                return;
            }

            Sprite.SavePoint = value;
            NotifyChanged();
        }
    }

    public bool Visible
    {
        get => Sprite.Visible;
        set
        {
            if (Sprite.Visible == value) {
                return;
            }

            Sprite.Visible = value;
            NotifyChanged();
        }
    }

    public bool PassThroughWalls
    {
        get => Sprite.PassThroughWalls;
        set
        {
            if (Sprite.PassThroughWalls == value) {
                return;
            }

            Sprite.PassThroughWalls = value;
            NotifyChanged();
        }
    }

    public bool ChasePlayer
    {
        get => Sprite.ChasePlayer;
        set
        {
            if (Sprite.ChasePlayer == value) {
                return;
            }

            Sprite.ChasePlayer = value;
            NotifyChanged();
        }
    }

    public double SpeedCellsPerSecond
    {
        get => Sprite.SpeedCellsPerSecond;
        set
        {
            if (Math.Abs(Sprite.SpeedCellsPerSecond - value) < 0.001) {
                return;
            }

            Sprite.SpeedCellsPerSecond = value;
            NotifyChanged();
        }
    }

    public double DetectionRadiusCells
    {
        get => Sprite.DetectionRadiusCells;
        set
        {
            if (Math.Abs(Sprite.DetectionRadiusCells - value) < 0.001) {
                return;
            }

            Sprite.DetectionRadiusCells = value;
            NotifyChanged();
        }
    }

    public double PatrolRadiusCells
    {
        get => Sprite.PatrolRadiusCells;
        set
        {
            if (Math.Abs(Sprite.PatrolRadiusCells - value) < 0.001) {
                return;
            }

            Sprite.PatrolRadiusCells = value;
            NotifyChanged();
        }
    }

    public bool PatrolCircuit
    {
        get => Sprite.PatrolCircuit;
        set
        {
            if (Sprite.PatrolCircuit == value) {
                return;
            }

            Sprite.PatrolCircuit = value;
            NotifyChanged();
        }
    }

    public double EngagementHysteresisCells
    {
        get => Sprite.EngagementHysteresisCells;
        set
        {
            if (Math.Abs(Sprite.EngagementHysteresisCells - value) < 0.001) {
                return;
            }

            Sprite.EngagementHysteresisCells = value;
            NotifyChanged();
        }
    }

    public double StoppingDistanceCells
    {
        get => Sprite.StoppingDistanceCells;
        set
        {
            if (Math.Abs(Sprite.StoppingDistanceCells - value) < 0.001) {
                return;
            }

            Sprite.StoppingDistanceCells = value;
            NotifyChanged();
        }
    }

    public double MaxHealth
    {
        get => Sprite.MaxHealth;
        set
        {
            var clamped = Math.Max(0.0, value);
            if (Math.Abs(Sprite.MaxHealth - clamped) < 0.001) {
                return;
            }

            Sprite.MaxHealth = clamped;
            if (Sprite.Health > clamped && clamped > 0.0) {
                Sprite.Health = clamped;
            }

            NotifyChanged();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Health)));
        }
    }

    public double Health
    {
        get => Sprite.Health;
        set
        {
            var upper = Sprite.MaxHealth > 0.0 ? Sprite.MaxHealth : double.MaxValue;
            var clamped = Math.Clamp(value, 0.0, upper);
            if (Math.Abs(Sprite.Health - clamped) < 0.001) {
                return;
            }

            Sprite.Health = clamped;
            NotifyChanged();
        }
    }

    public double AttackDamage
    {
        get => Sprite.AttackDamage;
        set
        {
            var clamped = Math.Max(0.0, value);
            if (Math.Abs(Sprite.AttackDamage - clamped) < 0.001) {
                return;
            }

            Sprite.AttackDamage = clamped;
            NotifyChanged();
        }
    }

    public bool RangedAttack
    {
        get => Sprite.RangedAttack;
        set
        {
            if (Sprite.RangedAttack == value) {
                return;
            }

            Sprite.RangedAttack = value;
            NotifyChanged();
        }
    }

    public double AttackRangeCells
    {
        get => Sprite.AttackRangeCells;
        set
        {
            var clamped = Math.Max(0.0, value);
            if (Math.Abs(Sprite.AttackRangeCells - clamped) < 0.001) {
                return;
            }

            Sprite.AttackRangeCells = clamped;
            NotifyChanged();
        }
    }

    public double AttackCooldownSeconds
    {
        get => Sprite.AttackCooldownSeconds;
        set
        {
            var clamped = Math.Max(0.1, value);
            if (Math.Abs(Sprite.AttackCooldownSeconds - clamped) < 0.001) {
                return;
            }

            Sprite.AttackCooldownSeconds = clamped;
            NotifyChanged();
        }
    }

    public double AttackFovDegrees
    {
        get => Sprite.AttackFovDegrees;
        set
        {
            var clamped = Math.Clamp(value, 1.0, 360.0);
            if (Math.Abs(Sprite.AttackFovDegrees - clamped) < 0.001) {
                return;
            }

            Sprite.AttackFovDegrees = clamped;
            NotifyChanged();
        }
    }

    public int AttackBurstShots
    {
        get => Sprite.AttackBurstShots;
        set
        {
            var clamped = Math.Max(1, value);
            if (Sprite.AttackBurstShots == clamped) {
                return;
            }

            Sprite.AttackBurstShots = clamped;
            NotifyChanged();
        }
    }

    public double AttackBurstPauseSeconds
    {
        get => Sprite.AttackBurstPauseSeconds;
        set
        {
            var clamped = Math.Max(0.1, value);
            if (Math.Abs(Sprite.AttackBurstPauseSeconds - clamped) < 0.001) {
                return;
            }

            Sprite.AttackBurstPauseSeconds = clamped;
            NotifyChanged();
        }
    }

    public bool Explosive
    {
        get => Sprite.Explosive;
        set
        {
            if (Sprite.Explosive == value) {
                return;
            }

            Sprite.Explosive = value;
            NotifyChanged();
        }
    }

    public double ExplosiveHitPoints
    {
        get => Sprite.ExplosiveHitPoints;
        set
        {
            var clamped = Math.Max(1.0, value);
            if (Math.Abs(Sprite.ExplosiveHitPoints - clamped) < 0.001) {
                return;
            }

            Sprite.ExplosiveHitPoints = clamped;
            NotifyChanged();
        }
    }

    public double ExplosionRadiusCells
    {
        get => Sprite.ExplosionRadiusCells;
        set
        {
            var clamped = Math.Max(0.0, value);
            if (Math.Abs(Sprite.ExplosionRadiusCells - clamped) < 0.001) {
                return;
            }

            Sprite.ExplosionRadiusCells = clamped;
            NotifyChanged();
        }
    }

    public double ExplosionDamage
    {
        get => Sprite.ExplosionDamage;
        set
        {
            var clamped = Math.Max(0.0, value);
            if (Math.Abs(Sprite.ExplosionDamage - clamped) < 0.001) {
                return;
            }

            Sprite.ExplosionDamage = clamped;
            NotifyChanged();
        }
    }

    public double ExplosionScaleCells
    {
        get => Sprite.ExplosionScaleCells;
        set
        {
            var clamped = Math.Max(0.05, value);
            if (Math.Abs(Sprite.ExplosionScaleCells - clamped) < 0.001) {
                return;
            }

            Sprite.ExplosionScaleCells = clamped;
            NotifyChanged();
        }
    }

    public string? ExplosionSpriteSet
    {
        get => Sprite.ExplosionSpriteSet;
        set
        {
            if (Sprite.ExplosionSpriteSet == value) {
                return;
            }

            Sprite.ExplosionSpriteSet = value;
            NotifyChanged();
        }
    }

    public string? DestroyedSpriteSet
    {
        get => Sprite.DestroyedSpriteSet;
        set
        {
            if (Sprite.DestroyedSpriteSet == value) {
                return;
            }

            Sprite.DestroyedSpriteSet = value;
            NotifyChanged();
        }
    }

    public double DestroyedScaleCells
    {
        get => Sprite.DestroyedScaleCells;
        set
        {
            var clamped = Math.Max(0.05, value);
            if (Math.Abs(Sprite.DestroyedScaleCells - clamped) < 0.001) {
                return;
            }

            Sprite.DestroyedScaleCells = clamped;
            NotifyChanged();
        }
    }

    public string DamageResponseType
    {
        get => Sprite.DamageResponse?.Type ?? (Sprite.Explosive ? "explode" : string.Empty);
        set
        {
            var normalized = value?.Trim() ?? string.Empty;
            var response = EnsureDamageResponse();
            if (response.Type == normalized) {
                return;
            }

            response.Type = normalized;
            NotifyChanged();
        }
    }

    public double DamageResponseHitPoints
    {
        get => Sprite.DamageResponse?.HitPoints ?? Sprite.ExplosiveHitPoints;
        set
        {
            var clamped = Math.Max(1.0, value);
            var response = EnsureDamageResponse();
            if (Math.Abs(response.HitPoints - clamped) < 0.001) {
                return;
            }

            response.HitPoints = clamped;
            NotifyChanged();
        }
    }

    public string? DamageEffectSpriteSet
    {
        get => Sprite.DamageResponse?.EffectSpriteSet;
        set
        {
            var response = EnsureDamageResponse();
            if (response.EffectSpriteSet == value) {
                return;
            }

            response.EffectSpriteSet = value;
            NotifyChanged();
        }
    }

    public string? DamageEffectAnimation
    {
        get => Sprite.DamageResponse?.EffectAnimation;
        set
        {
            var response = EnsureDamageResponse();
            if (response.EffectAnimation == value) {
                return;
            }

            response.EffectAnimation = value;
            NotifyChanged();
        }
    }

    public double DamageEffectScaleCells
    {
        get => Sprite.DamageResponse?.EffectScaleCells ?? Sprite.ExplosionScaleCells;
        set
        {
            var clamped = Math.Max(0.05, value);
            var response = EnsureDamageResponse();
            if (Math.Abs(response.EffectScaleCells - clamped) < 0.001) {
                return;
            }

            response.EffectScaleCells = clamped;
            NotifyChanged();
        }
    }

    public string? DamageDestroyedSpriteSet
    {
        get => Sprite.DamageResponse?.DestroyedSpriteSet;
        set
        {
            var response = EnsureDamageResponse();
            if (response.DestroyedSpriteSet == value) {
                return;
            }

            response.DestroyedSpriteSet = value;
            NotifyChanged();
        }
    }

    public double DamageDestroyedScaleCells
    {
        get => Sprite.DamageResponse?.DestroyedScaleCells ?? Sprite.DestroyedScaleCells;
        set
        {
            var clamped = Math.Max(0.05, value);
            var response = EnsureDamageResponse();
            if (Math.Abs(response.DestroyedScaleCells - clamped) < 0.001) {
                return;
            }

            response.DestroyedScaleCells = clamped;
            NotifyChanged();
        }
    }

    public string? DamageSound
    {
        get => Sprite.DamageResponse?.Sound;
        set
        {
            var response = EnsureDamageResponse();
            if (response.Sound == value) {
                return;
            }

            response.Sound = value;
            NotifyChanged();
        }
    }

    public double DamageRadiusCells
    {
        get => Sprite.DamageResponse?.RadiusCells ?? Sprite.ExplosionRadiusCells;
        set
        {
            var clamped = Math.Max(0.0, value);
            var response = EnsureDamageResponse();
            if (Math.Abs(response.RadiusCells - clamped) < 0.001) {
                return;
            }

            response.RadiusCells = clamped;
            NotifyChanged();
        }
    }

    public double DamageAmount
    {
        get => Sprite.DamageResponse?.Damage ?? Sprite.ExplosionDamage;
        set
        {
            var clamped = Math.Max(0.0, value);
            var response = EnsureDamageResponse();
            if (Math.Abs(response.Damage - clamped) < 0.001) {
                return;
            }

            response.Damage = clamped;
            NotifyChanged();
        }
    }

    public string Position => $"{Sprite.XCell:0.##}, {Sprite.YCell:0.##}";
    public string Facing => $"{Sprite.FacingDegrees:0.#} deg";
    public string HealthSummary => Sprite.MaxHealth > 0.0
        ? $"{Sprite.Health:0.#}/{Sprite.MaxHealth:0.#} hp"
        : "No hit points";
    public string MovementSummary => (Sprite.PatrolCircuit, Sprite.ChasePlayer) switch
    {
        (true, true) => "Patrol + chase",
        (true, false) => "Patrol",
        (false, true) => "Chase",
        _ => "Static"
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private EditorSpriteDamageResponse EnsureDamageResponse()
    {
        Sprite.DamageResponse ??= new EditorSpriteDamageResponse {
            Type = Sprite.Explosive ? "explode" : "break",
            HitPoints = Sprite.ExplosiveHitPoints,
            EffectSpriteSet = Sprite.ExplosionSpriteSet,
            EffectAnimation = Sprite.Explosive ? "explode" : "break",
            EffectScaleCells = Sprite.ExplosionScaleCells,
            DestroyedSpriteSet = Sprite.DestroyedSpriteSet,
            DestroyedScaleCells = Sprite.DestroyedScaleCells,
            RadiusCells = Sprite.ExplosionRadiusCells,
            Damage = Sprite.ExplosionDamage
        };
        return Sprite.DamageResponse;
    }

    /// <summary>
    /// Raises a change notification for every bound property. Used after the underlying
    /// <see cref="Sprite"/> model is mutated wholesale (for example by applying edited JSON).
    /// </summary>
    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    private void NotifyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Position)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Facing)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HealthSummary)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MovementSummary)));
    }
}
