# Terrain Generation Architecture

## Runtime Ownership

`IslandGenerator` is the scene-facing island orchestrator. It owns authoring configuration, island-local shape settings, generated chunk runtime records, and the high-level desired state for terrain chunks.

`TerrainChunkRuntimeData` owns per-chunk runtime state: stable island chunk ID, world bounds, current and desired LOD, dirty bounds, visibility state, CPU density samples, and generated mesh reference.

`TerrainChunkView` owns Unity-facing rendering components for one terrain chunk. It does not evaluate density or select LOD.

## Island Density Model

The island keeps the existing top-down footprint and underside profile as the primary silhouette controls.

The top surface is intentionally simple: base height, broad top noise, organic low-frequency height variation, optional fine detail, edge drop, and underside noise. Overhangs come from a small 3D density offset masked near the upper rim. That keeps most of the island readable as the same floating-island shape while giving Marching Cubes non-heightfield work at the edges.

The old authored/random terrain feature stack, lakes, rivers, and water simulation have been removed. Regeneration clears legacy generated water chunk children so old scene previews disappear on the next island rebuild.

## Scheduling and Dirty Regions

Terrain changes should call `IslandGenerator.NotifyTerrainModified(Bounds)`. The island expands the bounds by one terrain sample, marks only intersecting chunks dirty, and queues them in `IslandWorkScheduler`.

The scheduler prioritizes modified chunks first, then visible chunks, then LOD-transition chunks, then distance. Runtime chunk rebuilds are capped by `maxChunkBuildsPerFrame`.

## LOD and Visibility

LOD is configured by `TerrainLodLevel[]`; the number of levels is not hard-coded. Each level declares terrain sample resolution, mesh update frequency, collision intent, and shadow intent.

The current seam strategy is conservative: LOD levels should use sample densities that divide the highest terrain density so edge sample positions remain compatible in world space, and neighbour LOD differences should be kept small by distance thresholds. This does not yet generate explicit transition cells or skirts, so aggressive adjacent LOD differences can still show cracks on high-curvature surfaces. Add skirts or transition cells before using large LOD deltas in close view.

## CPU/GPU Boundary

Terrain density evaluation and Marching Cubes extraction remain on the CPU in this implementation. The current rendering architecture needs a CPU `Mesh`, so moving density evaluation alone to a compute shader would require GPU readback before CPU meshing. That readback would add synchronization, buffer ownership complexity, and latency without removing the CPU meshing bottleneck.

The existing grass path remains compute-driven because its generated blades stay on the GPU and are rendered with indirect procedural drawing. That is the right boundary for the current project.

A full terrain GPU path should move density evaluation, case classification, vertex emission, normal generation, and rendering/indirect draw preparation together so normal gameplay does not require synchronous readback. Keep the CPU path for editor diagnostics and unsupported hardware.

## Profiling Markers

Major stages are marked in `IslandProfiler`:

- `Island.Refresh`
- `Island.GenerateChunks`
- `Island.BuildChunk`
- `Island.MeshExtraction`
- `Island.LODUpdate`
- `Island.DirtyTerrain`

Use the Unity Profiler in Play Mode with allocation recording enabled to measure chunk rebuilds and LOD transitions.
