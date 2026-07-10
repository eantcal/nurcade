# Raycast Engine Refactoring Battle Plan

## Purpose

This document is the initial working plan for turning WinRayCast from a Windows demo application into a reusable ray-casting engine that can later be embedded into nuBASIC as a first-class graphics/language feature.

The immediate goal is not to rewrite everything. The goal is to carve out a clean, testable core while preserving the current demo behavior, then add missing game-facing features such as sprites, sprite movement, and a BASIC-facing API.

## Current Situation

WinRayCast is currently a compact Windows application with the engine, world model, texture loading, player movement, and presentation layer closely coupled together.

Key files:

- `RaycastEngine.h/.cpp`: ray traversal, wall projection, ceiling/floor rendering, transparent wall passes, shading, and final blit.
- `WorldMap.h/.cpp`: map matrix, cell metadata, texture table parsing from `res/world.ini`, and `HBITMAP` ownership references.
- `Player.h/.cpp`: camera/player position, angle lookup tables, movement, collision against wall cells.
- `BitmapBuffer.h/.cpp`: Windows bitmap extraction into a 32-bit pixel buffer.
- `DdxDevice.h/.cpp`: DirectDraw primary surface setup and frame presentation.
- `WinRayCast.cpp`: Win32 app lifecycle, keyboard input, resource loading, camera setup, render loop.
- `res/world.ini`: map cells and texture mapping.

nuBASIC already has a graphics subsystem and a BASIC-level raycast sample:

- `lib/api/nu_builtin_module_graphics.cc`: graphics built-in function/module registration.
- `include/api/nu_os_gdi.h` and `lib/api/nu_os_gdi.cc`: OS graphics bridge for Windows GDI and Linux/X11.
- `examples/graphics/raycast3d.bas`: existing BASIC raycast demo implemented in the language itself.

This means the long-term integration should target nuBASIC's existing graphics module and screen/double-buffering model, rather than creating a separate application loop.

## Main Architectural Problem

The current engine is not yet reusable because core raycasting depends directly on Windows types and rendering primitives:

- `HDC`, `HBITMAP`, `RECT`, `BYTE`, `DWORD`, `RGB`, `GetRValue`, `StretchDIBits`.
- DirectDraw availability via `DdxDevice::getInstance()`.
- Texture lookup is tied to `HBITMAP`.
- The frame buffer is allocated and owned inside `RaycastEngine`.
- Rendering and presentation happen in the same call.
- Input and movement are controlled from the Win32 sample app.

For nuBASIC, the engine should instead expose a platform-neutral C++ core that can render into a caller-provided pixel buffer or backend interface. The BASIC runtime should own when frames are produced, how input is read, and how the frame is presented.

## Target Shape

The desired end state is a layered design:

1. `raycast_core`
   Platform-independent C++ code for map data, camera/player state, ray traversal, projection math, z-buffer/depth data, wall rendering decisions, sprite projection, and software raster output.

2. `raycast_assets`
   Texture and sprite image abstractions based on plain RGBA/BGRA pixel buffers. Platform-specific loaders can live outside the core.

3. `raycast_backends`
   Optional adapters for Win32/GDI, DirectDraw legacy demo support, X11/nuBASIC graphics, or any future SDL/OpenGL bridge.

4. `winraycast_demo`
   A demo application that uses the reusable core but no longer owns the engine architecture.

5. `nubasic integration`
   Built-in statements/functions or a `graphics::raycast` module that lets BASIC programs create worlds, load textures, define sprites, move them, and render frames.

## Phase 0: Safety Net and Baseline

Before refactoring, create a reproducible baseline.

- Confirm the current Visual Studio solution builds.
- Capture at least one known-good screenshot of `res/world.ini`.
- Record current controls and runtime behavior.
- Add a small developer note describing map cell bit layout.
- Keep the initial demo behavior unchanged.

Deliverables:

- Build instructions for the current project.
- Baseline screenshot or checksum-based visual smoke test.
- Notes for current map cell encoding.

## Phase 1: Document and Name the Existing Data Model

The current map cell is a `uint64_t` with several packed fields. The renderer currently extracts at least:

- low byte: solid wall texture key.
- bits 8-15: ceiling texture key.
- bits 16-23: floor texture key.
- bits 24-31: transparent wall texture key.
- bits 32-39: raised/upper wall texture key.

This should be made explicit before changing code.

Actions:

- Introduce named masks and helper functions such as `wallTexture(cell)`, `ceilingTexture(cell)`, `floorTexture(cell)`, `transparentTexture(cell)`, `upperWallTexture(cell)`.
- Replace repeated magic masks like `0xff`, `0xff00`, `0x00ff0000`, `0xff000000`, and `0xff00000000UL`.
- Keep the binary map format compatible with `res/world.ini`.

Deliverables:

- `MapCell` helper or namespace.
- No behavioral change.
- Easier future BASIC serialization.

## Phase 2: Split Core Rendering from Presentation

Refactor `RaycastEngine::renderScene` so it no longer writes directly to a Windows/DC/DirectDraw destination.

Actions:

- Introduce a plain frame buffer type:
  - width
  - height
  - pitch
  - pixel format, initially 32-bit BGRA or RGBA
  - raw pixel span/vector
- Change rendering to write into that frame buffer.
- Move final presentation into a separate adapter:
  - current WinRayCast demo adapter uses `StretchDIBits` or DirectDraw.
  - nuBASIC adapter can reuse its existing GDI/X11 drawing path.
- Separate sky/background fill from Windows bitmap extraction.

Deliverables:

- `RaycastEngine::render(world, frameBuffer)` or equivalent.
- WinRayCast still runs.
- No `HDC`, `HBITMAP`, or `RECT` in the core render API.

## Phase 3: Replace Windows Texture Handles with Pixel Textures

Textures should become engine assets, not OS handles.

Actions:

- Introduce `Texture` as an immutable pixel buffer with width/height.
- Replace `WorldMap::applyTextureToPanel(int, HBITMAP)` with texture IDs mapped to `TextureHandle` or `shared_ptr<Texture>`.
- Move BMP/GDI loading to a Win32 demo loader.
- For nuBASIC, consider reusing the existing image loading path:
  - Windows: GDI+ image loading already exists in `nu_os_gdi.cc`.
  - Linux: `stb_image` based loading already exists.
- Keep a compatibility shim so existing `res/*.bmp` assets still load.

Deliverables:

- Core engine accepts texture data independent of Windows.
- Existing demo textures still render.
- Texture cache lifetime is owned outside the raycast core.

## Phase 4: Extract Camera and Movement Semantics

`Player` currently mixes camera projection tables, world collision, movement, and player state.

Actions:

- Rename or split concepts:
  - `Camera`: projection resolution, field of view, angle lookup tables, pitch/slope, projection center.
  - `Actor` or `PlayerController`: position, angle, movement.
  - `CollisionMap`: query solid cells.
- Keep a thin compatibility wrapper if useful.
- Make movement receive collision queries from the world instead of depending on `WorldMap` concrete type.

Deliverables:

- A camera object usable by nuBASIC without WinRayCast globals.
- Movement code that can later apply to sprites/actors too.

## Phase 5: Add Depth Buffer / Column Visibility Data

Sprites need correct occlusion against walls. The current wall render loop computes wall distance per projected column but does not persist that information as a reusable z-buffer.

Actions:

- During wall rendering, store per-column wall distance.
- Normalize fish-eye correction consistently before comparing sprite depth.
- Expose this internally as `std::vector<double> columnDepth`.
- Use it later for sprite clipping and occlusion.

Deliverables:

- No visual behavior change for walls.
- Internal z-buffer ready for sprite rendering.

## Phase 6: Introduce Static Sprites

Add billboard sprites rendered after walls and before final presentation.

Minimum sprite model:

- `id`
- `x`, `y`
- `z` or vertical offset, initially optional
- `textureId`
- `width`, `height` scale factors
- `visible`
- `transparentColor` or alpha handling

Rendering approach:

- Transform sprite world position into camera space.
- Reject sprites behind the camera or outside FOV.
- Project sprite width/height to screen.
- Draw vertical texture columns.
- Clip by screen bounds.
- Compare sprite distance against `columnDepth[x]`.
- Sort sprites far-to-near for correct sprite-to-sprite overlap.

Deliverables:

- Static sprites visible in the WinRayCast demo.
- Sprites correctly hidden behind walls.
- Transparent pixels skipped.

### Phase 6A: High-Resolution Sprite LOD Readiness

The first production-quality sprite sets should stay practical: `64`, `128`,
`256`, and `512` pixel frames are enough for most enemies and objects in a
classic ray-casting view. However, the engine should not bake `512x512` in as a
hard technical limit. Larger directional frames such as `1024x1024` can be
valuable for boss-sized sprites, close-up objects, high-resolution displays, or
future scripted scenes in nuBASIC.

This should be treated as an opt-in extension, not the default asset size.

Actions:

- Keep supported sprite resolutions metadata-driven through
  `supportedResolutions`, `defaultResolution`, `maxResolution`, and `lod`.
- Audit the C++ sprite metadata loader for validation ranges that reject
  resolutions above `512`, and replace hard limits with a configurable maximum
  or a documented engine constant.
- Add an engine-side warning or validation path for very large sprite frames so
  authors understand memory and bandwidth cost.
- Make sure LOD selection can choose `1024` only for very near distances while
  falling back to lower resolutions quickly.
- Avoid eager loading every direction at every high resolution unless the cache
  policy is explicit; prefer lazy loading or a bounded sprite texture cache
  before encouraging large packs.
- Extend editor validation to show sprite-set memory estimates and flag missing
  downscaled resolutions.
- Consider editor-side generation of lower-resolution variants from a high-res
  source frame.
- Keep BMP support for the initial implementation, but track PNG or an internal
  cached texture format as a likely follow-up once sprite frames grow beyond
  `512`.

Example metadata direction:

```json
{
  "supportedResolutions": [64, 128, 256, 512, 1024],
  "defaultResolution": 256,
  "maxResolution": 1024,
  "lod": [
    { "maxDistance": 1.25, "resolution": 1024 },
    { "maxDistance": 2.5, "resolution": 512 },
    { "maxDistance": 5.0, "resolution": 256 },
    { "maxDistance": 10.0, "resolution": 128 },
    { "maxDistance": 9999.0, "resolution": 64 }
  ]
}
```

Deliverables:

- Sprite metadata can describe resolutions above `512` without data-model
  changes.
- Renderer still uses distance-based LOD and does not sample large frames for
  far sprites.
- Editor reports memory/asset warnings for high-resolution sprite packs.
- Demo assets may remain capped at `512` unless there is a clear visual test
  case for `1024`.

## Phase 7: Sprite Movement and Collision

Once static sprites render correctly, introduce movement/update semantics.

Actions:

- Add velocity or explicit `moveSprite(id, dx, dy)`.
- Add collision options:
  - pass through walls
  - block against solid wall cells
  - optional radius-based collision
  - optional player/sprite collision callback later
- Add simple demo behavior: one sprite moving on a path or controlled by keys.

Deliverables:

- Moving sprite demo.
- Collision behavior documented.
- Foundation for BASIC commands.

### Phase 7A: Interactive Door Blocks

Doors should build on the structured block model rather than becoming free
sprites. A door is a cell-owned block with animated visual spans and explicit
movement state. This keeps collision, ray hits, transparent portals, elevators,
and level transitions tied to the same grid semantics as the rest of the map.

Actions:

- Keep wall-span visual behavior (`kind = solid|transparent`) separate from
  movement behavior (`passable = true|false`).
- Add optional block metadata for door state and animation without changing
  existing static blocks.
- Start with whole-cell doors: closed doors block movement, open doors become
  passable, and animation can slide or swap the wall texture over time.
- Let the editor author door blocks from the block inspector and preview their
  open/closed state.
- Later attach actions such as elevator movement, level transition target, or
  script callback.

Deliverables:

- Door block metadata round-trips through world JSON.
- Engine can toggle a door block from blocking to passable.
- Demo includes one animated door or lift entrance.

## Phase 8: nuBASIC API Design

The first nuBASIC integration should be simple and procedural, matching existing graphics commands. Object-oriented wrappers can come later.

Candidate BASIC API:

```basic
Screen 1

rc% = RaycastCreate(512, 512, 60)
RaycastCellSize rc%, 512, 512
RaycastMapLoad rc%, "world.ini"
RaycastTextureLoad rc%, &h01, "01.bmp"
RaycastTextureLoad rc%, &h02, "02.bmp"

RaycastCamera rc%, 2048, 2048, 0

sprite% = SpriteCreate(rc%, "guard.bmp", 2300, 2100)
SpriteMove sprite%, 2, 0
SpriteSetVisible sprite%, 1

Do
   k$ = InKey$()
   If k$ = "w" Then RaycastMove rc%, 10
   If k$ = "s" Then RaycastMove rc%, -10
   If k$ = "a" Then RaycastTurn rc%, -4
   If k$ = "d" Then RaycastTurn rc%, 4

   ScreenLock
   RaycastRender rc%, 0, 0
   ScreenUnlock
Loop While 1
```

Candidate module-style API for modern syntax:

```basic
Using Module graphics

world = raycast::create(512, 512, 60)
raycast::map_load world, "world.ini"
raycast::texture_load world, &h01, "01.bmp"
raycast::camera world, 2048, 2048, 0
guard = raycast::sprite_create(world, "guard.bmp", 2300, 2100)
raycast::render world, 0, 0
```

Initial recommendation:

- Start with procedural built-ins because they fit the current graphics module style.
- Add module exports under `graphics` or a new `raycast` module once naming conventions are settled.
- Keep returned handles as integers, similar to `BitmapLoad`.

## Phase 9: nuBASIC Integration Points

Likely files to touch in nuBASIC:

- `lib/api/nu_builtin_module_graphics.cc`: register BASIC functions.
- `include/api/nu_os_gdi.h`: expose any low-level blit operation needed by the raycast renderer.
- `lib/api/nu_os_gdi.cc`: implement cross-platform frame presentation.
- `lib/CMakeLists.txt`: add the raycast core source files.
- `examples/graphics/`: add a new `raycast_engine.bas` example.
- `wiki/Graphics-and-Multimedia.md` and `wiki/Command-Reference.md`: document commands.
- `tests/`: add text-mode-safe tests for argument validation and state management.

Important constraint:

- nuBASIC has `TINY_NUBASIC_VER` for console-only builds. Raycast API should either be excluded there, or available only as non-rendering state tests.

## Phase 10: Testing Strategy

Testing should start below the graphics layer.

Use GoogleTest as the unit-test framework and wire it through CMake/CTest. The
test target should stay independent from the Win32 demo application so it can
validate the reusable engine core without opening a window or requiring
DirectDraw.

Core tests:

- Map parsing and cell decoding.
- Ray intersection against small maps.
- Player/camera movement and collision.
- Projection math for known positions.
- Sprite projection and occlusion against a fixed depth buffer.

Visual smoke tests:

- Render a deterministic frame to an offscreen buffer.
- Hash the buffer or compare selected pixels.
- Keep tolerance-based comparison if texture loaders differ across platforms.

nuBASIC tests:

- Handles are created/freed correctly.
- Invalid handles fail cleanly.
- Function arity/type checks match existing built-in behavior.
- `TINY_NUBASIC_VER` behavior is explicit.

## Suggested Implementation Order

1. Add map-cell helpers and documentation.
2. Add initial GoogleTest/CTest wiring with a first map-cell unit test.
3. Introduce a platform-neutral `FrameBuffer`.
4. Refactor wall rendering to write to `FrameBuffer`.
5. Replace `HBITMAP` in core with `Texture`.
6. Preserve WinRayCast demo through a Win32 adapter.
7. Add per-column depth buffer.
8. Add static sprites.
9. Add moving sprites and collision policy.
10. Add an authoring editor for maps, textures, and sprite metadata.
11. Integrate minimal handle-based API into nuBASIC.
12. Replace or supplement `examples/graphics/raycast3d.bas` with a native-engine example.
13. Add a bottom HUD band with minimap, player position, and compass
    orientation using top-of-map as north.

## Open Questions

- Should the reusable engine live first inside WinRayCast, then be copied/imported into nuBASIC, or should it become a shared standalone library/submodule?
- Should texture pixels be RGBA, BGRA, or an explicit engine format with conversion at load time?
- Should BASIC maps use the existing packed `uint64_t` format, a friendlier textual DSL, or both?
- Should sprites be part of the raycast world only, or should nuBASIC expose a more general 2D sprite system too?
- Should the first nuBASIC API be classic BASIC statements/functions, modern module functions, or both?
- Should DirectDraw support be preserved in the demo, or replaced with GDI/modern presentation once the core is separated?
- Should the runtime HUD remain a software-rendered overlay in the core framebuffer, or should it become a backend-neutral overlay command list that Win32/OpenGL/Direct3D presenters can draw differently?

## Orientation And Runtime HUD Direction

The world coordinate convention should be explicit before the engine grows
status overlays and navigation aids:

- The top edge of the 2D map matrix is north.
- Increasing column is east.
- Increasing row is south.
- Decreasing column is west.

The first HUD milestone should reserve space outside the 3D viewport, then draw
a minimap, the player marker, health, weapon state, and a compass. The minimap
can start as a Win32 demo overlay while future work extracts the overlay as
backend-neutral draw commands so the same data can be presented by Win32 GDI,
OpenGL, Direct3D, or a nuBASIC host.

The current runtime bridge uses a fixed 1024x768 3D viewport inside a 1280x960
demo surface. The right and bottom bands are reserved for HUD content and are
not raycast-rendered, avoiding wasted CPU work for pixels that would otherwise
be overwritten by status UI.

## Multi-Map Worlds And Level Transitions

The current runtime still loads one world map at a time. Multi-map support
should be modeled above `WorldMap` as a session/project concept instead of
forcing several grids into one map object.

Recommended data model:

- A world package can contain multiple map files, each with a stable `id`,
  display name, and relative `.world.json` path.
- One map is marked as `startMap`.
- Transitions connect a source map to a target map through a trigger cell,
  normally an animated door/elevator block.
- A transition defines the target spawn position, target facing angle, and the
  transition style (`door`, `elevator`, `teleport`, or future scripted scene).

Recommended runtime flow:

1. `WorldSession` owns the current `WorldMap`, loaded scene metadata, sprite
   actors, player stats, current weapon, and shared asset caches.
2. Door proximity opens the door as it does today.
3. Once the player enters the transition trigger, the session enters a short
   transition state machine:
   `ClosingDoor -> TransferCamera -> LoadTargetMap -> OpeningDoor -> Active`.
4. Player health, weapon state, and global inventory survive the map switch.
5. Map-local actors are destroyed/recreated with the target map, unless a later
   design adds persistent actors.

Recommended editor flow:

- Show a package tree with maps and transitions.
- Let a transition be attached to a door block or a selected cell.
- Validate that each transition points to an existing target map and a valid
  target spawn cell.
- Preview the transition by exporting a temporary package and launching the
  runtime at the source map.

## First Concrete Milestone

The best first milestone is:

> Render the existing `res/world.ini` scene through a platform-neutral frame buffer while keeping the WinRayCast demo visually equivalent.

This creates the foundation for both later sprites and nuBASIC integration without changing the user-facing behavior too early.

## Progress Log

### 2026-06-03

The engine has crossed from "renderer + tooling" into a playable first-person
demo. Completed since the previous entry:

- **First-person weapons** (`src/engine/ViewWeapon.h/.cpp`): magazines, reserve
  ammo, reload, automatic/semi-auto fire timing, weapon bob, per-weapon range
  and damage. Multiple weapons per player, selected with the number keys; demo
  super shotgun force-reloads after two shots. Authored as `*.weapon.json` and
  editable in the WinRaycastEditor weapon panel.
- **Combat**: player health/stats; actor health, melee and ranged burst attacks,
  attack FOV, death animations; a new `ActorState::Attacking`. Weapon fire is a
  hitscan applying damage to the first actor along the view ray.
- **Weapon-noise alert**: firing temporarily widens nearby actors' detection
  radius (`noiseAlertSecondsRemaining` / `noiseAlertRadiusCells`), so loud
  weapons trade reach for stealth.
- **Interactive doors** (`WorldBlock.h::DoorDefinition`, `WorldMap` runtime door
  state + keyring): open on approach, optional required key, animate through
  texture frames, play a sound, auto-close.
- **HUD**: unlockable minimap (computer pickup), compass heading, player status
  panel, and a bottom teletype event log driven by world-configurable
  `MessageLogConfig` templates. Reserved right/bottom bands keep the 3D viewport
  free of HUD overdraw.
- **Audio** (`src/win/`): looping Ogg/Vorbis background music
  (`BackgroundMusicPlayer` + `OggVorbisDecoder`) and one-shot sound effects
  (`SoundEffectPlayer`) for weapons, doors, and pickups; runtime toggles.
- **Multi-layer worlds**: a world file can hold several `layers` (levels), each
  with its own grid, player start, block palette, cells, and sprite instances;
  `startLayer` / `activeLayer` select the entry level.
- **Pickups**: medikits (`pickupHealth`), ammo boxes, key cards (door keyring),
  and the map-unlock computer (`unlocksMap`), plus decorative props.
- **Tests**: added `ViewWeaponTests`; engine suite is now 14 gtest files,
  editor suite ~20 MSTest files.

These are documented in detail in `docs/engine-making/README.md` (§ X–§ XI) and
summarised in `README.md` (Features / Controls).

### 2026-05-17

Completed initial build-system migration:

- Added CMake project files and Visual Studio presets.
- Generated and built Debug/Release configurations with Visual Studio 2026 Professional.
- Removed legacy checked-in Visual Studio solution/project files.
- Documented CMake-based Visual Studio project generation in `README.md`.

Completed first refactoring steps:

- Added `MapCell` helpers for packed map-cell fields.
- Replaced key renderer/player magic masks with named `MapCell` helpers.
- Added GoogleTest/CTest integration.
- Added unit tests for map-cell decoding.
- Added a platform-neutral `FrameBuffer`.
- Moved `RaycastEngine` internal rendering output from raw `BYTE*` storage to `FrameBuffer`.
- Split `RaycastEngine::renderScene` into `renderToFrameBuffer` plus `presentFrameBuffer`.
- Added a platform-neutral `Texture` pixel buffer.
- Moved Windows bitmap extraction into `WinTextureLoader`.
- Replaced `WorldMap` texture storage from `HBITMAP` handles to `shared_ptr<Texture>`.
- Removed obsolete `BitmapBuffer`.
- Added unit tests for `FrameBuffer` and `Texture`.
- Removed `HDC` from the core `renderToFrameBuffer`, transparent-wall rendering,
  and texture stretch helpers.
- Replaced project header include guards with `#pragma once`, leaving generated
  `resource.h` untouched.
- Split the project into `WinRayCastEngine` static library plus `WinRayCast`
  demo executable.
- Moved DirectDraw frame presentation out of `RaycastEngine` into
  `WinFramePresenter`.
- Removed Win32 dependencies from the engine core sources and headers.
- Added `WinRayCastWinSupport` static helper library for Windows-only graphics
  support, keeping the demo thin and leaving room for future portable backends.
- Added `ColumnDepthBuffer` and wired wall rendering to store corrected
  per-column wall depth for future sprite occlusion.
- Added initial sprite data model for Doom-style directional views, including
  transparent-color metadata.
- Added `SpriteProjector` tests for billboard projection and wall-depth
  occlusion.
- Unified solid-wall ray hit lookup so each ray advances the nearest
  horizontal/vertical grid intersection first and can stop as soon as the
  closest solid wall is found.
- Extracted solid-wall ray lookup into a core `RayCaster` class with unit
  tests over synthetic maps.
- Added first billboard sprite rendering pass, including directional frame
  selection, transparent-color clipping, wall-depth occlusion, and far-to-near
  sprite ordering.
- Added MIT-licensed placeholder directional sprite BMPs for the demo.
- Added explicit sprite movement with optional solid-wall collision, ready to
  map onto a future BASIC-level `SpriteMove` command.
- Added metadata-driven sprite set groundwork: JSON metadata loading,
  validation, 8-direction definitions, LOD resolution selection in map-cell
  units, resolution fallback, and a `SpriteManager` frame-selection boundary.
- Added `WINRAYCAST_EDITOR_PLAN.md` for a C# WPF map/sprite authoring editor
  that can round-trip `world.ini`, validate sprite metadata, preview directional
  sprite sets, and export engine-ready data.
- Started RAII cleanup: DirectDraw COM objects, demo-owned engine/world
  instances, window DC acquisition, and bitmap loading now use scoped
  ownership helpers or smart pointers.

Current verification baseline:

- `cmake --preset vs2026-x64`
- `cmake --build --preset vs2026-x64-debug`
- `cmake --build --preset vs2026-x64-release`
- `ctest --test-dir out\build\vs2026-x64 -C Debug --output-on-failure`
- `ctest --test-dir out\build\vs2026-x64 -C Release --output-on-failure`
- Release smoke test: `WinRayCast` opens and responds.
