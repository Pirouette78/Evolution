# ACTIVE MISSIONS

## Mission 0: Infrastructure [DONE]
- [x] Setup URP 3D with Entities Package.
- [x] Implement `GameSpeedSystem` (Pause to 4x).
- [x] Create JSON loader for `TechTreeData`.

## Mission 1: The Cell [DONE]
- [x] Create `CellData` (Energy, Speed, PlayerID).
- [x] Implement `RandomMovementSystem` (Burst-compatible).
- [x] Setup the RGB Slime Map Compute Shader logic (`SlimeMapDispatcher`).
## Mission 2: Bootstrap [DONE]
- [x] Create `GlobalManagerAuthoring.cs` for prefabs and counts.
- [x] Implement `WorldBootstrapSystem` (on first frame spawning).
- [x] Prepare standard URP scene with 2D Camera.

## Mission 3: UI Integration [DONE]
- [x] Create UI Toolkit interface (Energy Label, Pause Button).
- [x] Connect Pause button to ECS `GameSpeedSystem`.

## Mission 4: Slime Map Visualization [DONE]
- [x] Integrate Compute Shader for Slime Trail simulation.
- [x] Connect `SlimeMapDispatcher` ECS data to the Compute Shader.
- [x] Render the resulting texture on screen.

## Mission 5: Tech Tree UI & Background Display [DONE]
- [x] Set up a Background SpriteRenderer to display the Slime Map texture.
- [x] Configure sorting layers to ensure background is behind ECS entities.
- [x] Integrate Tech Tree Data (DataLoader) into the UI.
- [x] Add interaction to spend Energy for researching Technologies.

## Mission 6: Adding Fun to Phase 1 [DONE]
- [x] Implement Chemotaxis (swarming towards food).
- [x] Implement Tech Impact (EvolutionSystem modifying Cell stats).
- [x] Implement The Green Menace (Player 2 faction & Cell vs Cell predation).