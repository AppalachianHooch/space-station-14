using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using System.Linq;
using System.Numerics;

namespace Content.IntegrationTests.Tests.Atmos;

/// <summary>
/// GasTileOverlay is being tested here
/// </summary>
public sealed class GasTileOverlayTemperatureNetworkingTest : AtmosTest
{
    protected override ResPath? TestMapPath => new("Maps/Test/Atmospherics/DeltaPressure/deltapressuretest.yml");

    [Test]
    public async Task TestGasOverlayDataSync()
    {
        var sMapSys = Server.System<SharedMapSystem>();

        var gridComp = ProcessEnt.Comp3;
        var gridNetEnt = Server.EntMan.GetNetEntity(ProcessEnt);

        var gridCoords = new EntityCoordinates(ProcessEnt, Vector2.Zero);
        var tileIndices = sMapSys.TileIndicesFor(ProcessEnt, gridComp, gridCoords);
        var mixture = SAtmos.GetTileMixture(ProcessEnt, null, tileIndices, true);

        // Get data for client side.
        var cGridEnt = CEntMan.GetEntity(gridNetEnt);
        Assert.That(CEntMan.TryGetComponent<GasTileOverlayComponent>(cGridEnt, out var cOverlay),
            "Client grid is missing GasTileOverlayComponent");

        // Check if the server actually sent the gas chunks
        Assert.That(cOverlay, Is.Not.Null, "Gas overlay is null on the client.");
        Assert.That(cOverlay.Chunks, Is.Not.Empty, "Gas overlay chunks are empty on the client.");

        //Start real tests
        await InjectHotPlasma(ProcessEnt, tileIndices, mixture, 400f);

        await CheckForInjectedGas(cOverlay, tileIndices, 400f);

        await InjectHotPlasma(ProcessEnt, tileIndices, mixture, 800f + ThermalByte.TempDegreeResolution - 1); // Rounding test

        await CheckForInjectedGas(cOverlay, tileIndices, 800f);

        await InjectHotPlasma(ProcessEnt, tileIndices, mixture, ThermalByte.TempMaximum + 200f); // This one hits max temperature

        await CheckForInjectedGas(cOverlay, tileIndices, ThermalByte.TempMaximum);

        await InjectHotPlasma(ProcessEnt, tileIndices, mixture, ThermalByte.TempMinimum);
        await InjectHotPlasma(ProcessEnt, tileIndices, mixture, ThermalByte.TempMinimum + (ThermalByte.TempDegreeResolution * 2) - 1); // Test the networking optimisation, this should not be networked yet

        await CheckForInjectedGas(cOverlay, tileIndices, ThermalByte.TempMinimum);

        await InjectHotPlasma(ProcessEnt, tileIndices, mixture, ThermalByte.TempMinimum + (ThermalByte.TempDegreeResolution * 2)); // This should

        await CheckForInjectedGas(cOverlay, tileIndices, ThermalByte.TempMinimum + (ThermalByte.TempDegreeResolution * 2));
    }

    [Test]
    public async Task TestGasOverlayChunkPruneSync()
    {
        var gridNetEnt = Server.EntMan.GetNetEntity(ProcessEnt);
        var cGridEnt = CEntMan.GetEntity(gridNetEnt);

        Assert.That(CEntMan.TryGetComponent<GasTileOverlayComponent>(cGridEnt, out var cOverlay),
            "Client grid is missing GasTileOverlayComponent");

        var chunkA = new Vector2i(0, 0);
        var chunkB = new Vector2i(1, 0);

        await Server.WaitPost(() =>
        {
            var sOverlay = SEntMan.GetComponent<GasTileOverlayComponent>(ProcessEnt);
            sOverlay.Chunks[chunkA] = new GasOverlayChunk(chunkA);
            sOverlay.Chunks[chunkB] = new GasOverlayChunk(chunkB);
            sOverlay.ForceTick = STiming.CurTick;
            SEntMan.Dirty(ProcessEnt, sOverlay);
        });

        await RunTicks(30);
        await Task.WhenAll(Client.WaitIdleAsync(), Server.WaitIdleAsync());

        await Client.WaitAssertion(() =>
        {
            Assert.That(cOverlay.Chunks.ContainsKey(chunkA), Is.True, "Expected chunk A to be present.");
            Assert.That(cOverlay.Chunks.ContainsKey(chunkB), Is.True, "Expected chunk B to be present.");
        });

        await Server.WaitPost(() =>
        {
            var sOverlay = SEntMan.GetComponent<GasTileOverlayComponent>(ProcessEnt);
            sOverlay.Chunks.Remove(chunkB);
            sOverlay.ForceTick = STiming.CurTick;
            SEntMan.Dirty(ProcessEnt, sOverlay);
        });

        await RunTicks(30);
        await Task.WhenAll(Client.WaitIdleAsync(), Server.WaitIdleAsync());

        await Client.WaitAssertion(() =>
        {
            Assert.That(cOverlay.Chunks.ContainsKey(chunkA), Is.True, "Expected chunk A to remain present.");
            Assert.That(cOverlay.Chunks.ContainsKey(chunkB), Is.False, "Expected chunk B to be pruned from client state.");
        });
    }

    private async Task CheckForInjectedGas(GasTileOverlayComponent overlay, Vector2i indices, float expectedTemp)
    {
        await Client.WaitPost(() =>
        {
            var chunkIndices = SharedGasTileOverlaySystem.GetGasChunkIndices(indices);

            Assert.That(overlay.Chunks.TryGetValue(chunkIndices, out var chunk), "Chunk not found");
            Assert.That(chunk, Is.Not.Null, "Chunk not found");

            // Calculate the exact index in the TileData array
            var localX = MathHelper.Mod(indices.X, SharedGasTileOverlaySystem.ChunkSize);
            var localY = MathHelper.Mod(indices.Y, SharedGasTileOverlaySystem.ChunkSize);
            int tileIndex = localX + localY * SharedGasTileOverlaySystem.ChunkSize;

            var tile = chunk.TileData[tileIndex];
            tile.ByteGasTemperature.TryGetTemperature(out var actualTemp);

            Assert.That(actualTemp, Is.EqualTo(expectedTemp).Within(0.01f), $"Tile at {indices} had wrong temperature!");
        });
    }

    private async Task InjectHotPlasma(EntityUid gridEnt, Vector2i tileIndices, GasMixture mixture, float temperature)
    {
        //Server makes atmos
        await Server.WaitPost(() =>
        {
            if (mixture != null)
            {
                mixture.Clear();
                mixture.AdjustMoles(Gas.Plasma, 100f); // Inject hot plasma
                mixture.Temperature = temperature;
                SAtmos.InvalidateVisuals(gridEnt, tileIndices);
            }
        });

        await RunTicks(60);
        await Task.WhenAll(Client.WaitIdleAsync(), Server.WaitIdleAsync());
    }
}
