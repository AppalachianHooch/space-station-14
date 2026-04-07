using System.Collections.Generic;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.Consoles;
using Robust.Shared.Maths;
using NUnit.Framework;

namespace Content.Tests.Shared.Atmos;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class AtmosStateReconcileTest
{
    [Test]
    public void GasTileOverlayDeltaState_ApplyToFullState_PrunesMissingKeys()
    {
        var keep = new Vector2i(0, 0);
        var remove = new Vector2i(1, 0);

        var state = new GasTileOverlayState(new Dictionary<Vector2i, GasOverlayChunk>
        {
            [keep] = new GasOverlayChunk(keep),
            [remove] = new GasOverlayChunk(remove),
        });

        var updatedKeep = new GasOverlayChunk(keep);
        var delta = new GasTileOverlayDeltaState(
            new Dictionary<Vector2i, GasOverlayChunk>
            {
                [keep] = updatedKeep,
            },
            new HashSet<Vector2i> { keep });

        Assert.DoesNotThrow(() => delta.ApplyToFullState(state));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(state.Chunks.ContainsKey(keep), Is.True);
            Assert.That(state.Chunks.ContainsKey(remove), Is.False);
            Assert.That(state.Chunks[keep].Index, Is.EqualTo(updatedKeep.Index));
        }
    }

    [Test]
    public void AtmosMonitoringDeltaState_ApplyToFullState_PrunesMissingKeys()
    {
        var keep = new Vector2i(0, 0);
        var remove = new Vector2i(1, 0);
        var subnet = new AtmosMonitoringConsoleSubnet(1, AtmosPipeLayer.Primary, Color.White);

        var stateChunks = new Dictionary<Vector2i, Dictionary<AtmosMonitoringConsoleSubnet, ulong>>
        {
            [keep] = new() { [subnet] = 1 },
            [remove] = new() { [subnet] = 2 },
        };

        var modifiedChunks = new Dictionary<Vector2i, Dictionary<AtmosMonitoringConsoleSubnet, ulong>>
        {
            [keep] = new() { [subnet] = 42 },
        };

        var helper = new AtmosMonitoringConsoleTestSystem();

        Assert.DoesNotThrow(() =>
            helper.ApplyDelta(stateChunks, modifiedChunks, new HashSet<Vector2i> { keep }));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stateChunks.ContainsKey(keep), Is.True);
            Assert.That(stateChunks.ContainsKey(remove), Is.False);
            Assert.That(stateChunks[keep][subnet], Is.EqualTo(42UL));
        }
    }

    private sealed class AtmosMonitoringConsoleTestSystem : SharedAtmosMonitoringConsoleSystem
    {
        public void ApplyDelta(
            Dictionary<Vector2i, Dictionary<AtmosMonitoringConsoleSubnet, ulong>> stateChunks,
            Dictionary<Vector2i, Dictionary<AtmosMonitoringConsoleSubnet, ulong>> modifiedChunks,
            HashSet<Vector2i> allChunks)
        {
            var state = new AtmosMonitoringConsoleState(stateChunks, new());
            var delta = new AtmosMonitoringConsoleDeltaState(modifiedChunks, new(), allChunks);
            delta.ApplyToFullState(state);
        }
    }
}
