# Terrain and Water Generation Architecture

## Runtime Ownership

`IslandGenerator` is the scene-facing island orchestrator. It owns authoring configuration, island-local feature settings, generated chunk runtime records, and the high-level desired state for terrain and water chunks.

`TerrainChunkRuntimeData` owns per-chunk runtime state: stable island chunk ID, world bounds, current and desired LOD, dirty bounds, visibility state, CPU density samples, and generated mesh reference.

`TerrainChunkView` owns Unity-facing rendering components for one terrain chunk. It does not evaluate density, select LOD, or run water simulation.

Water state remains in `WaterGrid` as flat contiguous arrays for ground height, depth, next-depth, flow velocity, and flags. `WaterChunk` now stores chunk runtime gates and throttling state, while `WaterSimulation` bridges Unity lifecycle to `WaterGroundSampler`, `WaterFlowSolver`, and `WaterMeshBuilder`.

## Scheduling and Dirty Regions

Terrain changes should call `IslandGenerator.NotifyTerrainModified(Bounds)`. The island expands the bounds by one terrain sample, marks only intersecting chunks dirty, queues them in `IslandWorkScheduler`, and notifies `WaterSimulation.NotifyTerrainChanged`.

The scheduler prioritizes modified chunks first, then visible chunks, then LOD-transition chunks, then distance. Runtime chunk rebuilds are capped by `maxChunkBuildsPerFrame`.

Water terrain resampling is bounded by the changed world bounds. Water mesh rebuilding respects per-chunk render gates and mesh update intervals.

## LOD and Visibility

LOD is configured by `TerrainLodLevel[]`; the number of levels is not hard-coded. Each level declares terrain sample resolution, water sample resolution intent, mesh/simulation frequency, water activity, water rendering, collision intent, and shadow intent.

`TerrainLodSelector` evaluates distance, camera frustum, behind-camera state, recent visibility, and active modification state. Recent visibility avoids rapid cull/rebuild churn near frustum edges. Hysteresis uses separate enter and exit distances.

The current seam strategy is conservative: LOD levels should use sample densities that divide the highest terrain density so edge sample positions remain compatible in world space, and neighbour LOD differences should be kept small by distance thresholds. This does not yet generate explicit transition cells or skirts, so aggressive adjacent LOD differences can still show cracks on high-curvature surfaces. Add skirts or transition cells before using large LOD deltas in close view.

## CPU/GPU Boundary

Terrain density evaluation and Marching Cubes extraction remain on the CPU in this implementation. The current rendering architecture needs a CPU `Mesh`, so moving density evaluation alone to a compute shader would require GPU readback before CPU meshing. That readback would add synchronization, buffer ownership complexity, and latency without removing the CPU meshing bottleneck.

The existing grass path remains compute-driven because its generated blades stay on the GPU and are rendered with indirect procedural drawing. That is the right boundary for the current project.

A full terrain GPU path should move density evaluation, case classification, vertex emission, normal generation, and rendering/indirect draw preparation together so normal gameplay does not require synchronous readback. Keep the CPU path for editor diagnostics and unsupported hardware.

Water simulation remains CPU-side because gameplay sampling, terrain-change coupling, and current mesh rendering all consume CPU data. Move it to compute only if downstream water rendering and gameplay queries can avoid frequent GPU-to-CPU transfers.

## Profiling Markers

Major stages are marked in `IslandProfiler`:

- `Island.Refresh`
- `Island.GenerateFeatures`
- `Island.GenerateChunks`
- `Island.BuildChunk`
- `Island.MeshExtraction`
- `Island.LODUpdate`
- `Island.DirtyTerrain`
- `Water.Initialize`
- `Water.GroundSample`
- `Water.Simulate`
- `Water.MeshBuild`

Use the Unity Profiler in Play Mode with allocation recording enabled to measure chunk rebuilds, LOD transitions, water terrain resampling, simulation, and mesh upload behavior.
