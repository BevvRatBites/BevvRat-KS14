using Content.Shared.KS14.TeslaGate;
using Content.Shared.Power;

namespace Content.Client.KS14.TeslaGate;

public sealed class TeslaGateSystem : SharedTeslaGateSystem
{
    public override void OnPowerChange(Entity<TeslaGateComponent> teslaGate, ref PowerChangedEvent args) => UpdateAppearance(teslaGate, args.Powered, teslaGate.Comp.CurrentlyShocking ? TeslaGateVisualState.Active : TeslaGateVisualState.Inactive);

    /// <inheritdoc/>
    public override void Enable(Entity<TeslaGateComponent> teslaGate) { }

    /// <inheritdoc/>
    public override void Disable(Entity<TeslaGateComponent> teslaGate)
    {
        UpdateAppearance(teslaGate, true, TeslaGateVisualState.Inactive);
    }
}
