using Content.Shared.KS14.TeslaGate;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using System.Runtime.CompilerServices;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;
using Content.Server.AlertLevel;
using Content.Shared.Power;

namespace Content.Server.KS14.TeslaGate;

// dont let ilya see this dogshit code
public sealed class TeslaGateSystem : SharedTeslaGateSystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audioSystem = default!;
    [Dependency] private readonly DamageableSystem _damageableSystem = default!;
    [Dependency] private readonly SharedPowerReceiverSystem _powerReceiverSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TeslaGateComponent, StartCollideEvent>(OnGateStartCollide);
        SubscribeLocalEvent<AlertLevelChangedEvent>(OnAlertLevelChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<TeslaGateComponent>();
        while (query.MoveNext(out var uid, out var teslaGateComponent))
        {
            var teslaGate = (uid, teslaGateComponent);
            var canShock = CanWork(uid, teslaGateComponent, out var canStart);

            if (teslaGateComponent.IsTimerWireCut)
                continue;

            if (teslaGateComponent.CurrentlyShocking)
            {
                if (!canShock || IsFinishedShocking(teslaGateComponent))
                    QuitZappinEmAll(teslaGate);

                continue;
            }

            if (!canStart)
                continue;

            teslaGateComponent.PulseAccumulator += frameTime;

            if (teslaGateComponent.PulseAccumulator < teslaGateComponent.PulseInterval.TotalSeconds)
                continue;

            if (canShock)
                ZapEmAll(teslaGate);
        }
    }

    // im not gonna give it `ent<comp>` and then just convert that back to `entuid, comp` if im inlining it, that just sounds stupid
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool CanStartWork(EntityUid uid, TeslaGateComponent teslaGateComponent)
    {
        if (!_powerReceiverSystem.IsPowered(uid))
            return false;

        return true;
    }

    // HELP
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool CanWork(EntityUid uid, TeslaGateComponent teslaGateComponent, out bool canStart)
    {
        canStart = CanStartWork(uid, teslaGateComponent);
        if (!teslaGateComponent.Enabled)
            return false;

        if (!canStart)
            return false;

        return true;
    }

    private void ZapEmAll(Entity<TeslaGateComponent> teslaGate)
    {
        var (uid, teslaGateComponent) = teslaGate;
        teslaGateComponent.PulseAccumulator = 0;
        teslaGateComponent.LastShockTime = _gameTiming.CurTime;

        _audioSystem.PlayPvs(teslaGateComponent.ShockSound, uid);

        UpdateAppearance(teslaGate, true, TeslaGateVisualState.Active);
        Dirty(teslaGate);

        teslaGateComponent.CurrentlyShocking = true;
        foreach (var entity in _physicsSystem.GetContactingEntities(uid))
            CollideAct(teslaGateComponent, entity);
    }

    private void QuitZappinEmAll(Entity<TeslaGateComponent> teslaGate)
    {
        var (uid, teslaGateComponent) = teslaGate;

        teslaGateComponent.CurrentlyShocking = false;
        teslaGateComponent.ThingsBeingShocked.Clear();

        UpdateAppearance(teslaGate, false, teslaGateComponent.Enabled ? TeslaGateVisualState.Ready : TeslaGateVisualState.Inactive);
        Dirty(teslaGate);

        _audioSystem.PlayPvs(teslaGateComponent.StartingSound, uid);
    }

    private void ResetAccumulator(TeslaGateComponent teslaGateComponent)
    {
        teslaGateComponent.PulseAccumulator = 0f;
        teslaGateComponent.LastShockTime = TimeSpan.MinValue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Zap(EntityUid uid, DamageSpecifier damage)
    {
        _damageableSystem.TryChangeDamage(uid, damage, ignoreResistances: true);
    }

    /// <inheritdoc/>
    public override void Enable(Entity<TeslaGateComponent> teslaGate)
    {
        var (uid, teslaGateComponent) = teslaGate;

        ResetAccumulator(teslaGateComponent);
        teslaGateComponent.Enabled = true;

        if (CanStartWork(uid, teslaGateComponent))
        {
            _audioSystem.PlayPvs(teslaGateComponent.StartingSound, uid);

            UpdateAppearance(teslaGate, false, TeslaGateVisualState.Ready);
            Dirty(teslaGate);
        }
    }

    /// <inheritdoc/>
    public override void Disable(Entity<TeslaGateComponent> teslaGate)
    {
        var (uid, teslaGateComponent) = teslaGate;

        teslaGateComponent.Enabled = false;
        ResetAccumulator(teslaGateComponent);

        UpdateAppearance(teslaGate, false, TeslaGateVisualState.Inactive);
        Dirty(teslaGate);
    }

    // idgaf actually
    public override void OnPowerChange(Entity<TeslaGateComponent> teslaGate, ref PowerChangedEvent args) { }

    private void CollideAct(TeslaGateComponent teslaGateComponent, EntityUid otherEntity)
    {
        if (!teslaGateComponent.ThingsBeingShocked.Add(GetNetEntity(otherEntity)))
            return;

        var damage = new DamageSpecifier(_prototypeManager.Index<DamageTypePrototype>("Shock"), teslaGateComponent.ShockDamage);
        Zap(otherEntity, damage);
    }

    private void OnGateStartCollide(Entity<TeslaGateComponent> teslaGate, ref StartCollideEvent args)
    {
        var (uid, teslaGateComponent) = teslaGate;

        if (teslaGateComponent.CurrentlyShocking)
            CollideAct(teslaGateComponent, args.OtherEntity);
    }

    private void OnAlertLevelChanged(AlertLevelChangedEvent alertEvent)
    {
        if (!TryComp<AlertLevelComponent>(alertEvent.Station, out var alertLevelComponent))
            return;

        var alertLevel = alertEvent.AlertLevel;

        var query = EntityQueryEnumerator<TeslaGateComponent>();
        while (query.MoveNext(out var uid, out var teslaGateComponent))
        {
            var teslaGate = (uid, teslaGateComponent);

            if (teslaGateComponent.IsForceHacked)
                continue;

            if (teslaGateComponent.EnabledAlertLevels.Contains(alertLevel))
                Enable(teslaGate);
            else
                Disable(teslaGate);
        }
    }
}
