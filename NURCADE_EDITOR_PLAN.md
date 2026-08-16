# nuRCADE Editor Plan

## Purpose

The editor should make nuRCADE content authoring practical before the engine
is imported into nuBASIC. It should let a user edit maps, assign textures, place
sprites, validate sprite metadata, preview directional sprite sets, and export
engine-ready data files without hand-editing `world.ini` or JSON.

The editor is not part of the runtime engine. It is a tooling subproject that
shares file formats and validation rules with the engine.

## Recommended Technology

Use a C# WPF desktop application targeting .NET 8.

Reasons:

- Visual Studio Professional and .NET Desktop are already available locally.
- WPF is mature for Windows authoring tools and does not require extra MAUI
  workloads.
- It supports a rich 2D editing surface, tree/property panels, drag/drop, image
  previews, and keyboard shortcuts with less friction than WinForms.
- The editor can live in the same repository without changing the C++ runtime
  architecture.

Suggested project layout:

```text
tools/
  NuRcade.Editor/
    NuRcade.Editor.sln
    src/
      NuRcade.Editor/
      NuRcade.Editor.Core/
      NuRcade.Editor.Tests/
```

The C++ CMake build should not depend on the editor. The editor can be opened
directly in Visual Studio, while a later optional CMake target can call
`dotnet build` for CI convenience.

## Scope

The editor should cover three authoring domains:

1. Map authoring
   - Edit the grid used by the raycast world.
   - Assign wall, ceiling, floor, transparent wall, and wall segment texture
     IDs.
   - Preserve compatibility with the packed `uint64_t` cell format as a legacy
     import/export path, but move the long-term authoring model to a versioned
     JSON world format.
   - Support arbitrary grid dimensions and configurable cell dimensions.
   - Support wall spans with arbitrary bottom/top heights instead of only the
     current one-block/two-block wall convention.

2. Texture and sprite-set authoring
   - Manage texture keys and source bitmap files.
   - Load and validate sprite metadata JSON.
   - Preview 8 directional views and available resolutions.
   - Detect missing bitmap files, invalid resolutions, invalid direction angles,
     and unsupported formats.

3. Scene authoring
   - Place sprite instances on the map.
   - Edit sprite position, facing angle, scale, visibility, collision radius,
     and sprite set reference.
   - Validate wall collision and bounds.

## Improvement Roadmap

### Editor Track

1. Sprite placement ergonomics
   - Keep sprite grid markers synchronized when editing `xCell` / `yCell` in
     the sprite inspector.
   - Add nudge buttons or keyboard nudging for fine movement.
   - Add click-to-place and drag-to-move behavior in the sprite layer.
   - Show sprite preview thumbnails directly on occupied cells.

2. Block editing
   - Make the block inspector editable instead of read-only.
   - Support adding/removing wall spans.
   - Edit span kind, texture, bottom/top heights, and collision flag.
   - Add block creation/duplication so cells can switch to authored block ids
     without editing packed texture fields directly.

3. World package workflow
   - Open/save self-contained world packages as the primary workflow.
   - Keep `world.ini` as import/export compatibility only.
   - Validate every referenced texture, sprite metadata file, and sprite bitmap
     relative to the world package root.

4. Authoring feedback
   - Add validation overlays on the grid.
   - Highlight sprites outside bounds, in solid cells, or missing sprite sets.
   - Add an asset browser tab for texture/sprite package contents.
   - Add an editor-side 3D preview tab. The first pass can use WPF
     `Viewport3D`, which is Direct3D-backed on Windows, to show grid geometry,
     floor/ceiling planes, structured wall spans, sprite markers, and player
     start without coupling the editor to the C++ raycaster.

### Engine Track

1. World package loading
   - Treat `.world.json` as the canonical runtime scene entry point.
   - Load sprite sets and sprite instances declared directly in the world JSON.
   - Keep project JSON loading as a compatibility wrapper while the editor
     converges on world packages.

2. Structured block rendering
   - Move rendering decisions away from legacy packed-cell approximations.
   - Render arbitrary wall spans, transparent spans, raised ledges, and partial
     blockers from `WorldBlockDefinition`.
   - Preserve legacy packed-cell behavior only as imported compatibility data.

3. Sprite rendering
   - Select sprite resolution through metadata LOD at render time instead of
     preloading only one resolution.
   - Add optional alpha-aware image paths while preserving BMP color-key support.
   - Add sprite depth/collision validation tests against structured blocks.

4. Runtime tooling hooks
   - Add a lightweight diagnostics mode for loaded world packages.
   - Report missing assets with package-relative paths.
   - Expose loaded world/sprite counts for smoke tests and editor launch flows.

## Data Files

### Existing World File

The editor should initially read and write the current `res/world.ini` format so
the existing demo keeps working.

It should display each packed cell as named fields:

- solid wall texture
- ceiling texture
- floor texture
- transparent wall texture
- upper wall texture

`world.ini` is now considered a legacy compatibility format. It should remain
importable for the current demo map and optionally exportable for simple maps,
but it cannot represent arbitrary wall heights or richer per-cell metadata
without awkward packed-bit extensions.

### Canonical World JSON

Introduce a versioned JSON world file as the canonical editor and engine map
format. The project file should point to this file once the engine supports it,
for example:

```json
{
  "format": "nurcade.world",
  "version": 1,
  "name": "demo_world",
  "grid": {
    "columns": 16,
    "rows": 16,
    "cellWidth": 512,
    "cellDepth": 512,
    "defaultWallHeight": 512
  },
  "playerStart": {
    "xCell": 2.5,
    "yCell": 2.5,
    "facingDegrees": 0
  },
  "textures": {
    "01": { "name": "brick", "file": "textures/01_brick.bmp" },
    "02": { "name": "stone", "file": "textures/02_stone.bmp" },
    "ff": { "name": "sky", "file": "clouds.bmp" }
  },
  "cells": [
    [
      {
        "floor": { "texture": "09", "height": 0 },
        "ceiling": { "texture": "0a", "height": 512 },
        "walls": [
          {
            "kind": "solid",
            "texture": "01",
            "bottom": 0,
            "top": 512,
            "collision": true
          }
        ]
      }
    ]
  ]
}
```

The first implementation can constrain cells to zero or one primary solid wall
span plus optional transparent wall metadata, but the data model should allow a
list of vertical spans so later work can support stacked walls, raised ledges,
windows, fences, doors, and partial-height blockers without changing the file
shape.

Recommended cell semantics:

- `floor.height` and `ceiling.height` are world units relative to the cell base.
- A wall span is rendered from `bottom` to `top` in world units.
- `top - bottom` may be any positive value, not just `cellDepth`.
- `kind` controls visual ray behavior: `solid` participates in the opaque wall
  pass, while `transparent` is collected by the transparent-wall pass.
- `passable` controls movement/collision only. A transparent span can still be
  blocking, and a solid-looking span can be passable for special effects.
- `faceTextures` optionally overrides the span texture per block side:
  `north`, `east`, `south`, and `west`. Missing face entries fall back to the
  span's `texture`, so existing one-texture blocks remain valid.
- Texture IDs are symbolic strings in JSON, even when they represent legacy
  hex keys.
- Legacy packed values are derived only when exporting to `world.ini`.
- Missing optional values are filled from `grid.defaultWallHeight` and project
  defaults during load.

### Door Blocks

Doors should be modelled as block definitions rather than ordinary sprites. A
door occupies a grid cell, can block movement while closed, can become passable
when open, and can still render as a transparent or solid wall span depending
on the art. Sprite-style animation is still useful for the door panel itself,
but the gameplay unit should remain the block so elevators, level exits, and
portal frames can share collision and transition logic.

First-pass door metadata should be block-local and optional, for example:

```json
{
  "name": "lift_door",
  "walls": [
    {
      "kind": "solid",
      "texture": "42",
      "faceTextures": {
        "north": "42",
        "east": "43",
        "south": "44",
        "west": "45"
      },
      "bottom": 0,
      "top": 512,
      "passable": false
    }
  ],
  "door": {
    "closedPassable": false,
    "openPassable": true,
    "openTimeMs": 350,
    "animation": "slide_up",
    "targetWorld": "level02/demo.world.json",
    "targetPlayerStart": "lift_exit"
  }
}
```

The editor should expose this later as a block inspector section. The engine
should initially treat the optional `door` object as metadata, then add runtime
state (`closed`, `opening`, `open`, `closing`) once the static passable/visual
split is stable.

### New Project File

Add a higher-level editor project file later, for example:

```json
{
  "project": "demo_world",
  "worldFile": "world.ini",
  "textureRoot": ".",
  "spriteSets": [
    "sprites/monster.sprite.json"
  ],
  "spriteInstances": [
    {
      "name": "guard_01",
      "spriteSet": "doom_style_monster",
      "xCell": 5.5,
      "yCell": 4.5,
      "facingDegrees": 0,
      "scaleCells": 1.0,
      "collisionRadiusCells": 0.2,
      "visible": true
    }
  ]
}
```

Coordinates in the editor should be cell-based by default. Export can convert
to engine world units using the map cell size.

### Sprite Metadata

The editor should understand the metadata format now being introduced in the
engine:

- sprite set name
- format, initially BMP
- transparent color
- supported resolutions
- default and max resolution
- 8 direction definitions
- LOD rules expressed in map-cell distance

The editor should be able to create, open, validate, and save this metadata.

## UI Concept

Use a workbench layout rather than a game-like interface:

The editor should borrow from three families of tools:

- Wolfenstein-style grid editors for the primary map representation: the map is
  a rectangular grid and each cell/block owns wall, floor, ceiling, object, and
  collision metadata.
- Doom Builder-style workflow for authoring modes and property inspection:
  separate modes for geometry/cells, textures, things/sprites, validation, and
  export; selecting an item shows all editable properties in an inspector.
- Tiled-style layer organization: wall layer, floor/ceiling layer, sprite/object
  layer, horizon/sky metadata, and validation overlays can be shown, hidden, or
  edited independently.

For nuRCADE this means the cell remains the canonical editable unit, but it
is displayed as a composite tile:

- base floor/ceiling state
- solid/transparent/upper wall state
- optional two-block-high wall badge
- zero or more sprite/object markers
- optional horizon/sky marker for open spaces
- validation warnings

- Top toolbar:
  - new/open/save
  - validate
  - export
  - undo/redo
  - zoom
  - active tool selector

- Left panel:
  - map layers
  - texture palette
  - sprite sets
  - sprite instances

- Center:
  - grid editor canvas
  - pan/zoom
  - selectable cells and sprite markers
  - optional FOV/camera preview overlay

- Right panel:
  - property inspector for selected cell, texture, sprite set, or sprite
    instance

- Bottom panel:
  - validation messages
  - missing assets
  - export warnings

## Map Editing Features

Phase 1 should provide:

- Open current `world.ini`.
- Display map as a grid.
- Select cells.
- Paint texture fields with a palette:
  - wall
  - floor
  - ceiling
  - transparent wall
  - upper wall
- Edit packed cell value directly for advanced/debug use.
- Resize map with explicit row/column controls.
- Save back to `world.ini`.
- Validate:
  - rectangular map
  - valid hex cell values
  - texture IDs referenced by cells exist

## Sprite Features

Phase 1 should provide:

- Load sprite metadata JSON.
- Validate all referenced BMP files exist.
- Show all 8 directions in a grid:
  - front
  - front_right
  - right
  - back_right
  - back
  - back_left
  - left
  - front_left
- Show available resolutions per direction.
- Warn when a direction lacks a preferred resolution and will use fallback.
- Preview transparency by checkerboard background.
- Show selected LOD resolution for a manually adjustable test distance.
- Show memory estimates for high-resolution sprite packs, especially when a
  sprite set declares frames above `512x512`.
- Warn when high-resolution frames are missing lower-resolution LOD variants.

Phase 2 should add:

- Create/edit sprite metadata from the UI.
- Allow future generation of lower-resolution variants from a high-resolution
  source frame, so authors can provide `1024` source art while the engine still
  has efficient `512`, `256`, `128`, and `64` LOD frames.
- Add/remove sprite set references in the project.
- Place sprite instances on the map.
- Edit facing angle and preview selected directional frame from a chosen camera
  position.
- Validate sprite collision radius against solid cells.

## Engine Integration Strategy

Keep the editor and engine loosely coupled:

- Editor has its own C# parser/model for authoring.
- Engine keeps the C++ runtime parser/validator.
- Tests should use shared fixture files where useful so both sides agree on
  expected data.

The editor can launch the C++ demo through the exported project file, but it
should not embed the renderer until the engine API is stable.

Later options:

- Export a deterministic frame request and compare against the C++ renderer.
- Add a native preview bridge only after the engine API is stable. The current
  WPF `Viewport3D` preview should remain a fast authoring view, not a
  replacement for validating the real raycast renderer.
- Share JSON schema files between editor and engine tests.

## Validation Rules

Map validation:

- Rows have equal length.
- Cell values fit in `uint64_t`.
- Texture IDs used by the map are known.
- Player/camera start is inside bounds and not inside a solid wall.
- Sprite instances are inside bounds.

Sprite metadata validation:

- Format is supported, initially only BMP.
- Exactly 8 supported direction names are present.
- Direction angles match the expected 45-degree increments.
- Referenced resolution keys are declared in `supportedResolutions`.
- Referenced files exist.
- Transparent color has three values in range `0..255`.
- LOD rules are positive and ordered by distance after load.
- LOD resolutions are declared in `supportedResolutions`.

Export validation:

- Output paths are relative to the selected project root.
- No missing assets.
- No duplicate sprite instance names.
- No sprite instances inside solid walls unless explicitly marked
  pass-through.

## Milestones

### Milestone 1: Editor Skeleton

- Create WPF solution under `tools/NuRcade.Editor`.
- Add core C# library with map/sprite metadata models.
- Add unit test project.
- Open/save a simple editor project JSON.
- Display an empty grid and property panel.

### Milestone 2: World Map Round Trip

- Parse current `res/world.ini`.
- Render grid cells.
- Select and inspect packed cell fields.
- Save without changing formatting-sensitive meaning.
- Add tests for current world file round-trip.

### Milestone 3: Texture Palette

- Load texture mapping from `world.ini`.
- Preview BMP textures.
- Paint wall/floor/ceiling fields.
- Validate texture references.

### Milestone 4: Sprite Metadata Viewer

- Open sprite metadata JSON.
- Validate metadata using editor-side rules equivalent to the C++ loader.
- Preview 8 directions and resolutions.
- Preview transparency.
- Show LOD selection by distance in cells.

### Milestone 5: Sprite Placement

- Add sprite instances to the project file.
- Place/move/rotate sprite markers on the grid.
- Edit sprite properties.
- Validate collision radius and bounds.

### Milestone 6: Export To Demo/Engine

- Export `world.ini`.
- Export sprite metadata JSON.
- Export sprite instance file or future engine scene file.
- Update nuRCADE demo to optionally load the exported scene description.

### Milestone 7: Canonical JSON World Format

- Add `WorldDocument` / `WorldCell` / `WallSpan` models in editor core.
- Add JSON reader/writer with schema-version checks.
- Add conversion from legacy `world.ini` packed cells into the JSON model.
- Keep `world.ini` export for maps that fit the legacy limitations.
- Update the project JSON so `worldFile` can point to either `.ini` or
  `.world.json`, with a type/version check at load time.
- Add editor controls for grid rows, columns, cell width, cell depth, default
  wall height, floor height, ceiling height, and selected wall span bottom/top.
- Add validation for non-rectangular grids, invalid heights, inverted wall
  spans, overlapping spans, unknown textures, and player/sprite placement
  outside walkable height ranges.

### Milestone 8: Engine Variable-Height Walls

- Introduce a C++ runtime map model that stores structured cells instead of
  only packed `uint64_t` values.
- Keep a legacy adapter from packed cells into the structured runtime model.
- Add a C++ `WorldJsonLoader` with tests shared against editor fixtures.
- Refactor raycasting hit results to carry wall span bottom/top heights,
  texture ID, collision behavior, and transparency metadata.
- Update wall projection so screen-space top/bottom are computed from the
  camera height, wall bottom, wall top, distance, and projection scale.
- Keep the current one-block wall rendering visually equivalent through tests
  before enabling arbitrary heights.
- Extend sprite collision/visibility rules so sprites can stand in cells with
  raised floors, low ceilings, windows, or partial blockers.

### Milestone 9: Editor 3D Preview

- Add a central editor tab for a Direct3D-backed WPF `Viewport3D` preview.
- Build the preview from the same editor `WorldDocument` / block palette used
  by save/export, including arbitrary wall spans where available.
- Show floor planes, ceiling planes, wall volumes, transparent spans, sprite
  markers, and the player start marker.
- Keep the preview editor-only and intentionally coarse at first.
- Later add alpha visualization, sprite billboard previews, picking, and richer
  camera controls.

## Testing Plan

Editor tests:

- `world.ini` parse and save round-trip.
- world JSON parse/save round-trip.
- legacy `world.ini` to world JSON conversion.
- world JSON validation for arbitrary cell sizes and wall heights.
- Packed cell encode/decode.
- Sprite metadata validation.
- LOD resolution fallback.
- Project JSON load/save.
- Missing file and invalid direction diagnostics.

Manual tests:

- Open current demo world.
- Paint a wall texture and save.
- Open the result in nuRCADE.
- Load a sprite set and verify all 8 views.
- Place a sprite and export.

## Open Questions

- Should the editor project file become the long-term canonical scene format,
  with `world.ini` only as an export target?
- Should the canonical world JSON include sprite instances directly, or should
  it stay focused on static world geometry while project JSON owns sprites?
- Should wall heights be expressed in world units, cell-height fractions, or
  both through editor UI helpers?
- Should a cell support multiple independent wall spans in Milestone 7, or only
  model the list while the first engine pass renders one span?
- Should texture IDs remain numeric/hex in the UI, or should the editor expose
  symbolic names?
- Should sprite instances live in the world file, in a separate scene JSON, or
  inside a broader project JSON?
- Should we eventually embed a native renderer preview, or keep the first editor
  strictly as an authoring/validation tool?

## Immediate Recommendation

Start with Milestone 1 and Milestone 2. This gives us a usable project skeleton
and proves we can safely round-trip the existing map before adding richer sprite
authoring.

## Progress Log



Started Milestone 9:

- Added a first WPF `Viewport3D` preview tab in the central editor workspace.
- Preserved cell block ids when loading canonical `world.json`, so editor
  tools can reason from structured block definitions instead of only the legacy
  packed texture fields.
- Added preview generation for floor planes, ceiling planes, wall span volumes,
  transparent spans, sprite markers, and the player start marker.
- Added real texture materials for floor, ceiling, and wall preview geometry
  using the same package-relative texture resolution as the editor palette.
- Added sprite billboards to the 3D preview using the same sprite-set previews
  already shown on the 2D map layer.
- Added first camera presets for angled, top-down, and player-start views.
- Added 3D hit-testing so clicking preview geometry selects the matching cell,
  while clicking a billboard selects the sprite instance in the editor.
- Added selection highlights in the 3D preview for the active cell and active
  sprite instance.
- Added editor tests proving the shipped demo world builds a non-empty 3D
  preview model, that the camera presets are wired, and that preview hit
  targets select editor objects.



Started Milestone 1:

- Created `tools/NuRcade.Editor` solution.
- Added WPF shell project targeting .NET 8 Windows.
- Added `NuRcade.Editor.Core` for editor-side models and parsing.
- Added `NuRcade.Editor.Tests` with MSTest coverage.
- Added packed cell encode/decode model matching the C++ engine layout.
- Added editor map model that can represent:
  - multiple sprites per block/cell
  - floor and ceiling texture fields for non-solid cells
  - optional horizon image metadata
  - upper-wall/two-block-high wall detection
- Added current `world.ini` parser and tests proving the existing demo map can
  be read by the editor core.
- Added first WPF grid shell that loads and displays the current map.
- Updated the editor direction to combine Wolfenstein-style cell editing, Doom
  Builder-style modes/property inspection, and Tiled-style layers.

Continued Milestone 2:

- Added editor-side `world.ini` writer.
- Added parse/serialize/parse round-trip test to protect packed cell meaning
  before enabling destructive save operations in the UI.
- Added a Core document service and enabled WPF `Save As` for exporting a
  `world.ini` copy through the editor UI.
- Added WPF `Open` for selecting alternate `world.ini` files and reloading the
  grid, inspector, and validation state.

Started Milestone 3:

- Added editor-side texture palette resolution from `tmap` entries to BMP files
  next to the loaded `world.ini`.
- Added texture palette view models and WPF preview list.
- Added tests that verify current demo texture references resolve to existing
  bitmaps.
- Added editable selected-cell texture fields for wall, floor, ceiling,
  transparent wall, and upper wall.
- Added quick cell actions for clearing wall fields and clearing floor/ceiling
  fields.
- Added palette-driven painting for the selected cell with explicit paint
  targets for wall, floor, ceiling, transparent wall, and upper wall.
- Added a serialization test proving edited cell fields are preserved through
  `world.ini` write/read round-trip.
- Enabled the WPF Validate command.
- Extended editor validation to report texture mappings whose BMP files are
  missing on disk.

Started Milestone 4:

- Added editor-side sprite metadata JSON loader and validator aligned with the
  C++ loader rules for format, transparent color, supported resolutions,
  direction names/angles, referenced bitmap files, and LOD rules.
- Added tests for valid sprite metadata, unsupported formats, missing sprite
  bitmap files, and incomplete direction sets.
- Added a WPF command for opening sprite metadata JSON and a first directions
  viewer that lists direction name, angle, and available resolutions.
- Added `res/sprite_test.sprite.json` as a real metadata fixture for the
  existing directional test sprite BMPs.
- Extended the WPF sprite direction viewer to show a bitmap preview for each
  direction.
- Added sprite LOD selection preview with a distance slider and editor-side
  tests for resolution selection.
- Added color-key transparency preview for sprite direction thumbnails using
  the metadata-defined transparent RGB value and a checkerboard background.
- Added editor-side sprite resolution fallback selection matching the engine
  behavior, and surfaced the selected/fallback resolution per direction while
  scrubbing the LOD distance slider.

Started Milestone 5:

- Added a first WPF action for placing the loaded sprite set into the selected
  map cell as a cell-centered sprite instance.
- Added a sprite instance list and kept selected-cell sprite badges in sync
  after placement.
- Added core document helpers for adding and removing sprite instances while
  keeping per-cell sprite references synchronized.
- Added sprite instance selection, removal, and an editable WPF sprite
  inspector for name, position, facing angle, scale, collision radius,
  visibility, and pass-through wall behavior.
- Extended sprite validation for duplicate instance names, non-positive scale,
  negative collision radius, and sprite instances placed inside solid wall
  cells without pass-through enabled.

- Added an `EditorProjectDocument` model and JSON load/save service covering
  the project name, world file reference, texture root, sprite set references,
  and sprite instance list described in the editor project file section of
  this plan.
- Added editor tests for project file round-trip, default sprite field values,
  missing-file diagnostics, invalid JSON diagnostics, and the
  `FromMapDocument` helper.
- Added WPF `Open Project` and `Save Project As` commands that load the
  project, follow its `worldFile` reference to populate the map, and persist
  the current sprite instance list relative to the chosen project location.
- Registered the path of any sprite metadata opened from the WPF UI into
  `Document.SpriteSetFiles` (relative to the world file), so saved projects
  carry their sprite set references and an `Open Project` automatically
  reloads them.
- Added `SpriteMetadataWriter` and a real-fixture round-trip test that
  serializes the metadata back to JSON and reloads it through the existing
  loader.
- Added a `Save Sprite Set As` WPF command that lets the editor write the
  currently loaded sprite metadata to disk.
- Added a native C++ `SceneLoader` that parses the editor project file format
  in the nuRCADE engine library, with gtest coverage for valid projects,
  defaults, missing files, and invalid JSON.
- Extended the nuRCADE demo to accept an optional project JSON path on the
  command line; when provided, it loads the project's world file and
  instantiates sprites from the project's sprite sets and sprite instances
  instead of the hardcoded test sprite, so a scene authored in the editor can
  be opened directly by the engine.
- Improved the map grid layout so cells stretch horizontally with the available
  editor space instead of keeping a fixed pixel width.
- Added layer-aware map-cell texture previews: when the wall or floor/ceiling
  layer is selected and the referenced bitmap exists, the grid cell shows the
  bitmap instead of the packed numeric value.
- Added map grid zoom controls that adjust cell height while preserving
  width-stretch behavior across the available editor space.
- Added a sprite-layer cell marker that shows the number of sprite instances
  in each cell when the Sprites layer is selected.
- Split the combined Floor/Ceiling map layer into distinct Floor and Ceiling
  layers so each surface can be previewed and inspected independently.
- Kept palette painting in step with layer changes: selecting Floor, Ceiling,
  or Walls updates the active paint target accordingly.
- Added a Horizon-layer cell marker for cells that use horizon/sky metadata.
- Added a paint-while-selecting mode so choosing cells in the map grid can
  immediately apply the selected palette texture to the active paint target.
- Reworked map zoom to scale the whole map grid uniformly on both axes instead
  of changing only cell height.
- Replaced transform-based map zoom with square-cell sizing derived from the
  available map width, column count, and zoom factor so wall tiles no longer
  stretch into rectangular cells.
- Added an editor scene exporter that writes a `world.ini` next to an exported
  project JSON and rewrites sprite set paths relative to the export location.
- Enabled the WPF `Export` command so authored scenes can be saved as a
  project/world pair consumable by the demo/engine project loader.
- Added a nuRCADE demo menu command (`File > Open Project...`) that lets the
  running C++ engine load an exported project JSON, world map, sprite metadata,
  and sprite instances without restarting or passing a command-line argument.
- Kept the command-line project loading path intact and added a WPF `Test 3D`
  command that exports a temporary preview project, locates the CMake-built
  nuRCADE demo executable, and launches the engine with that project path so
  authored worlds can be tested directly from the editor.
- Made editor export/previews self-contained: `world.ini` texture references are
  rewritten into an exported `textures/` directory, `clouds.bmp` is copied next
  to the exported world, sprite metadata is copied into `sprites/`, and sprite
  bitmap paths are rewritten to copied assets so the C++ engine can launch from
  a temporary preview directory without depending on the original world folder.
- Started the canonical world JSON migration by adding editor-core
  `WorldDocument`, `WorldJsonDocumentService`, and `LegacyWorldConverter`
  support with tests for JSON round-trip, legacy packed-cell conversion, and
  invalid variable-height wall spans.
- Added editor commands for opening `*.world.json`, saving the current map as
  `*.world.json`, and loading project files whose `worldFile` points to either
  legacy `world.ini` or the new JSON world format.

Reorganized the world definition around a reusable block palette (Milestone 7
follow-up) and propagated it through the engine:

- Bumped the world JSON schema to version 2: cells are now a matrix of
  hex-string block ids that reference a `blocks` dictionary holding floor,
  ceiling, horizon, and an ordered list of variable-height wall spans per
  block. Removed inline per-cell wall data from the schema.
- Updated `WorldDocument`, `WorldJsonDocumentService`, and
  `LegacyWorldConverter` so loading/saving world JSON works on the block
  palette directly; `LegacyWorldConverter.FromEditorMap` now infers the
  unique block palette from the packed legacy cells, and `ToEditorMap` carries
  the palette back into the editor for inspection.
- Added an empty-block sentinel (`00`) and palette validation rules for
  unknown block ids, missing blocks, and invalid wall spans.
- Replaced the v1 JSON round-trip tests with v2 tests for palette round-trip,
  legacy packed-cell → palette inference, palette → editor map conversion,
  invalid wall span detection, and unknown block id detection.
- Added a `Block Palette` panel and a `Block Inspector` to the WPF editor that
  show the current palette derived from the map (ids, names, floor/ceiling,
  horizon, and the ordered list of wall spans with their bottom/top heights).
- Added `WorldBlock.h` to the engine with `BlockId`, `WallSpan`,
  `BlockSurface`, and `BlockDefinition` types, plus a parallel block layout
  on `WorldMap` (palette and per-cell block ids) that lives next to the
  existing packed-cell storage so the legacy `world.ini` path keeps working.
- Added `WorldJsonLoader` (C++) that parses world.json v2, populates the
  `WorldMap` block palette and per-cell block ids, and derives a packed-cell
  fallback for back-compat with the existing renderer; covered by
  `tests/WorldJsonLoaderTests.cpp`.
- Extended `RayCaster::RayHit` with a pointer to the hit block definition and
  updated `RaycastEngine::renderToFrameBuffer` to iterate the block's wall
  spans, projecting each span between its `bottom` and `top` world heights
  rather than relying on the fixed solid/upper-wall pair. Cells without a
  block definition fall back to the original solid + upper-wall rendering so
  the existing demo map continues to look the same.
- Hooked the engine demo (`nuRCADE.cpp`) up so a project pointing at a
  `*.world.json` world file is loaded through `WorldJsonLoader`; legacy
  `world.ini` paths still flow through `WorldMap::load`.

### Packaging

Added a single distributable layout driven by CMake + CPack:

- New variable-height demo world `res/demo.world.json` plus a thin
  `res/demo.nurcadeproj.json` project that references it. The world ships in
  the canonical v2 block-palette format and exercises stacked walls, tall
  walls, low partial walls, and an open-sky region so both the engine and the
  editor have something non-trivial to render straight after install.
- Added a gtest case that loads the shipped `res/demo.world.json` through
  `WorldJsonLoader` and asserts at least one wall span exceeds the cell depth,
  so the bundled fixture cannot regress silently.
- Added a CMake custom target `NuRcade.EditorPublish` that runs
  `dotnet publish` for `NuRcade.Editor.csproj` with
  `-r win-x64 --self-contained -p:PublishSingleFile=true`. The output ends up
  in `<build>/editor_publish/NuRcade.Editor.exe` and is consumed by the
  installer rules. The target is registered as `ALL` so a plain
  `Build Solution` in Visual Studio also produces the editor binary.
- Integrated the editor solution into the CMake-generated Visual Studio
  workspace: `include_external_msproject` adds
  `NuRcade.Editor.Core.csproj`, `NuRcade.Editor.csproj`, and
  `NuRcade.Editor.Tests.csproj` as native `Any CPU` projects inside the
  generated `nuRCADE.sln`/`.slnx`, organized under an "Editor" folder
  (engine projects sit under "Engine"). Engineers can now open one solution
  and find engine, editor, tests, and the installer `PACKAGE` target side by
  side.
- Added CPack rules with `NSIS;ZIP` generators, component split into
  `engine` (required) and `editor`, and Start-menu shortcuts for the demo
  world, the original world, and the editor. ZIP keeps third-party headers
  out (`INSTALL_GTEST`, `INSTALL_GMOCK`, and `JSON_Install` are forced off).
- Result of `cpack -G ZIP`: a 33-entry archive with `nuRCADE.exe`,
  `editor/NuRcade.Editor.exe`, `res/` (textures, world.ini, demo world JSON,
  demo project JSON, sprite metadata), and `LICENSE.txt`.

To produce the installer locally:

1. Configure once: `cmake -S . -B out/build/vs2026-x64 -G "Visual Studio 18 2026" -A x64`
   (any recent Visual Studio generator works; .NET 8 SDK and a recent CMake
   are required, NSIS is required only for the `.exe` installer).
2. Build Release: `cmake --build out/build/vs2026-x64 --config Release` —
   this builds the engine, the demo, the editor csproj, and runs
   `dotnet publish` for the editor in one shot.
3. Package: `cpack --config out/build/vs2026-x64/CPackConfig.cmake -C Release`.
   With NSIS installed, this produces `nuRCADE-<ver>-win-x64-setup.exe`
   alongside the ZIP; without NSIS, only the ZIP is produced. The `PACKAGE`
   project inside the generated solution is equivalent and can be built from
   inside Visual Studio.

### Source Layout And Structured Blocks

Started the engine source-layout cleanup and tightened the structured block
runtime path:

- Moved C++ engine files under `src/engine`, Windows presentation/texture
  helpers under `src/win`, and the demo application entrypoint under `src/app`.
  Resource files, icons, and `res/` stay at repository root so the Windows
  resource compiler and existing runtime asset paths remain simple.
- Updated CMake source lists, include directories, source groups, and test
  include paths to the new layout.
- Filtered generated C# `bin/` and `obj/` files out of the CMake editor source
  glob so Visual Studio does not reconfigure because of temporary WPF build
  artifacts.
- Kept the v2 world JSON model as the engine-facing format: a block palette
  (`blocks`) owns structured wall spans/surfaces, and the cell matrix (`cells`)
  stores block ids instead of packed numeric cells.
- Updated ray casting and world collision checks so structured blocks with any
  solid wall span are treated as ray hits even when their legacy packed cell
  approximation has no lower-wall byte. This is required for elevated or
  multi-span wall blocks.
- Added a C++ test covering an elevated-only solid span so the new block model
  cannot regress back to packed-cell-only behavior.
- Converted the editor right-side inspector into tabs (`Cell`, `Sprite`,
  `Block`, `Validation`) so block and sprite editing can grow without forcing
  every control into one vertical panel.
- Confirmed JSON parsing/writing uses the third-party `nlohmann_json` library
  in C++ and `System.Text.Json` in the C# editor.
- Removed the obsolete `miptknzr` dependency from CMake and the repository.
  The only remaining user was the legacy `world.ini` reader, which now uses a
  small local compatibility parser for `map {}` and `tmap {}` while all new
  world data goes through JSON.

### Self-Contained World Packages

Updated the demo asset layout so runnable worlds are isolated from examples and
legacy fixtures:

- The default demo now lives under `res/worlds/demo_embedded/`.
- `demo.world.json` is the canonical entry point and includes texture paths,
  sprite metadata references, and sprite instances directly.
- Wall textures and the sky bitmap live under `textures/` inside the world
  package.
- The generated monster sprite set lives under
  `sprites/procedural_monster/`, including metadata, 512px BMP runtime frames,
  alpha PNG previews, and local license notes.
- The root `res/world.ini`, root demo project JSON, and root texture/sprite
  files were removed from the main runtime layout.
- Legacy `world.ini` and placeholder sprite fixtures were moved under
  `res/examples/` so compatibility tests stay available without mixing old
  files into the demo world.
- The engine default launch path now opens
  `res/worlds/demo_embedded/demo.world.json`; loading that file directly also
  loads the declared sprite set and sprite instances.

### Editor And Engine Improvement Pass

Started the next editor-focused improvement pass:

- Added live sprite relocation in the editor: editing a sprite instance's
  `xCell` or `yCell` now moves its marker between map cells immediately and
  refreshes validation.
- Made the Block Inspector editable for block name, floor texture/height,
  ceiling texture/height, horizon image, and existing wall-span fields.
- Added editable wall-span properties for kind, texture, bottom/top height, and
  collision flag so the editor can author the structured block data used by the
  engine.
- Added player/camera start authoring next to sprite authoring: the editor now
  exposes a Player tab, a Player map layer, a grid marker, direct x/y/facing
  editing, and a "place at selected cell" action.
- Propagated `playerStart` through world JSON conversion, editor project JSON,
  temporary scene export, and the C++ project/world loaders so Test 3D and the
  runtime demo can start from the authored camera position and facing angle.
- Added cell copy/paste plus undo/redo support in the editor. Cell operations
  copy the map content fields (wall/floor/ceiling/transparent/upper/horizon)
  without cloning sprite or player entities, and the history tracks cell edits,
  paste operations, and player start edits.
- Split the saturated left workbench panel into tabs for Map, Assets, Sprites,
  and Help so layer controls, block/texture palettes, sprite tooling, and
  legend content no longer compete in one long vertical stack.
- Added an editor-side 3D Preview tab that builds a lightweight WPF viewport
  from the loaded world package: textured floors, ceilings, wall spans, sprite
  billboards, player start marker, and selection overlays all come from the
  same world data used by the engine.
- Exposed authoring cameras for angled, top-down, and perspective preview
  modes, with rotate, zoom, fit-all, and perspective navigation controls. The
  viewport also accepts mouse-wheel zoom and keyboard navigation
  (`W/S`, arrows, `A/D`, `Q/E`, `+/-`, `Home`) when focused.
- Added 3D preview layer switches for floors, ceilings, walls, sprites,
  player marker, and grid so map structure can be inspected without visual
  clutter. The two rotation controls are now contextual: in `Angled` view they
  rotate the map orbit, while in the other views they rotate the player-style
  perspective camera.
- Made the 3D preview movement controls contextual as well: in `Angled` view,
  `Up`, `Down`, `Left`, and `Right` pan the viewport across the map without
  changing the orbit angle; in perspective mode the same controls keep moving
  the preview camera through the world.
- Started replacing packed-number cell editing with block-oriented authoring:
  the Cell tab now shows a focused 3D preview of the selected cell and exposes
  texture pickers for lower wall, upper wall, transparent wall, floor, and
  ceiling. Editing through these controls clones the current cell block into a
  new palette entry, assigns that new block to the cell, and leaves the source
  block untouched for the rest of the map.
- Extended the same preview workflow to the Block Inspector: selected blocks
  now have their own focused 3D preview, floor/ceiling/span textures use the
  shared texture picker, and both local previews support rotate, zoom, pan, and
  fit controls independent from the whole-map 3D preview.
- Started moving map editing toward layer-driven context panels: when `Walls`,
  `Floor`, or `Ceiling` is selected, the Map tab now exposes the active
  cell/block texture controls and block palette; when `Sprites` is selected,
  the Map tab shows the sprites in the selected cell, keeps the selected sprite
  preview in sync, and selecting a sprite also selects the cell that owns it.
- Tightened those context panels so wall/floor/ceiling layers expose only the
  relevant texture controls, renamed the old asset tab into a shared Library,
  and moved core selected-sprite properties (position, facing, scale, movement,
  and visibility flags) into the `Sprites` map layer workflow.
- Synced the sprite animation preview workflow with the selected map sprite:
  choosing a sprite instance now selects the matching sprite metadata file, so
  the `Sprites` tab animation player shows frames from the same sprite set
  instead of whichever metadata file was selected last.
- Fixed canonical world JSON round-tripping from the editor: texture file names
  now preserve their original image extensions instead of being normalized back
  to `.bmp`, and tests cover both cell block ids and shipped demo texture paths
  so saved worlds reload in the engine with the same blocks and assets.
- Fixed the remaining world JSON save corruption for v2 block palettes: block
  id `00` is no longer forced to mean empty when the loaded world declares it
  as a real block, and unused palette blocks are preserved so editor saves do
  not silently remove authoring choices or break engine rendering.

### Orientation, Minimap, And Compass Plan

The map now needs an explicit orientation model shared by the editor and the
runtime HUD. For v2 worlds, the 2D editor convention should be:

- `north` is the top edge of the `cells` matrix.
- `east` is increasing column.
- `south` is increasing row.
- `west` is decreasing column.

Planned engine overlay:

- Add a bottom HUD band outside the main 3D render viewport, so gameplay
  rendering keeps its current aspect and future information panels do not
  cover the scene.
- Render a compact minimap in that band using block ids or simplified colors,
  with the player marker at the current world position.
- Draw a compass indicator tied to player facing, with `N`, `E`, `S`, and `W`
  derived from the world orientation above.
- Keep the first version software-rendered into the existing framebuffer; a
  later backend can move the same overlay primitives to OpenGL or Direct3D.

Planned editor work:

- Show a small orientation badge on the 2D map canvas: `N` at the top, `E` on
  the right, `S` at the bottom, `W` on the left.
- In 3D preview, rotate the compass labels with the angled preview camera when
  the view is orbiting, while the underlying world orientation remains fixed.
- Add a minimap preview panel that mirrors the engine HUD layout, so authored
  worlds can be checked before launch.
- Persist future orientation metadata only when the world needs non-default
  rotation; until then, the default top-is-north convention is enough.

## Status (2026-06-03)

Most of the roadmap above has shipped. The editor is a .NET 8 WPF application
(`NuRcade.Editor.Core` + WPF host + MSTest suite of ~20 test files) built as
part of the unified CMake/Visual Studio solution. Beyond the milestones already
logged, the editor now also:

- **Authors weapons.** `WeaponMetadataLoader` / `WeaponMetadataWriter` /
  `WeaponMetadataDocument` round-trip `*.weapon.json`, and a
  `WeaponAnimationPlaybackController` previews idle/fire/reload clips with the
  same transport used for sprites.
- **Edits raw JSON inline.** A docked, syntax-aware JSON pane
  (`JsonEditorPanelViewModel` + Scintilla host) stays in sync with the
  structured editor; `JsonSpanLocator` maps a selection to its byte span and
  `JsonEditingBackupService` guards against bad hand-edits.
- **Imports textures.** `TextureImport` brings external bitmaps into a world
  package and rewrites the palette to package-relative paths.
- **Handles multi-layer worlds.** Layers (`EditorLayer`) round-trip through the
  world JSON, including per-layer sprite instances (`LayerSpriteRoundTripTests`).
- **Ships the 3D preview** (Milestone 9): textured floors/ceilings/wall spans,
  sprite billboards, player marker, selection overlays, multiple camera presets,
  and hit-testing back to cells/sprites.

The corresponding engine HUD (minimap, compass, bottom event log) is implemented
in the demo application; see `docs/engine-making/README.md` § XI.3. Door blocks
(§ XI.2) are authored from the block model and round-trip through world JSON.
