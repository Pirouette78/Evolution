CONSULTER PRIORITAIREMENT : VISION_AND_STORY.md pour le contexte narratif et le flow du jeu.

# Project: Macro-Evolution (Working Title)
**Stack:** Unity 6+, ECS (Entities), URP, C#, Burst Compiler.
**Workflow:** Agentic AI via MCP (CoplayDev).

## Core Concept
A multi-phase evolutionary game (Micro to Galactic) with indirect gameplay. 
The player evolves entities via a tech tree to conquer environments.

## Architecture Rules
- **Data-Driven:** All stats (speed, cost, tech tree, etc.) must be in external data files (JSON/ScriptableObjects) for modding.
- **ECS-Only:** No Monobehaviours for gameplay. Use IComponentData and ISystem.
- **Phase Management:** Use a Tag-based system (CellTag, ZombieTag, PlanetTag, GalaxyTag) to switch simulation logic.
- **Scale:** High-performance simulation (thousands of entities). Use Slime Map (Compute Shaders) for macro view.
- **RGB Slime Map:** Use Compute Shaders to render player density (R=P1, G=P2, B=P3).
- **Asynchronous Phases:** Players advance independently through 4 official phases.
- **Multiplayer:** 3 players per instance, server-authoritative.

## The Zoom Logic (LOD Sim)
- Hybrid rendering: Entities at low zoom, Slime Map at high zoom.
- Use `CameraScale` component to toggle entity visibility and network sync.
Logic de Zoom (LOD Simulation)
Le zoom doit être géré par un système qui observe la distance de la caméra :
Zoom < 50 units : Les entités ECS sont visibles (Rendu par Entities.Graphics). La simulation est précise.
Zoom > 50 units : Les entités individuelles sont désactivées visuellement. Seule la Slime Map (RenderTexture via Compute Shader) est affichée.
Culling : Le serveur ne synchronise plus les positions des cellules au-delà d'un certain zoom pour économiser la bande passante, il n'envoie que la mise à jour de la Texture de Densité.


## Roadmap
1. Phase 1: Cellular (Eat, Divide, Evolve).
2. Phase 2: Slime/Macro View (Tactical Overlay).
3. Phase 3: Human/Zombie (Infection, Intelligence, World Map).
4. Phase 4+: Space/Galactic.

## Multiplayer Competition (RGB Sharding)
- **Grouping:** 3 players max per simulation instance.
- **Victory Condition:** Reach X% infection to eliminate opponents and promote to the next Scale (Human -> Planet -> Galaxy).
- **Visualization:** Use RGB color channels in the Slime Map Compute Shader to represent each player's influence.
- **Netcode:** Server-Authoritative. Only data for the current Scale instance is synchronized to the 3 connected clients.

## Scaling & Persistence
- **Asynchronous Progression:** Players advance to the next Phase independently. The "Macro Map" (Planet/Galaxy) is persistent and shared.
- **Spectator Mode:** High-level view uses Texture Streaming of the Slime Maps (Low-bandwidth, High-visuals).
- **Visual Style:** Organic, Shader-based (Reaction-Diffusion/Physarum). Focus on "Contemplative Tactical Maps".

## Technical Challenge: Time Synchronization
- Each Phase instance runs its own simulation clock.
- The Macro Map aggregates data from multiple active sub-instances.

## Localization & Text
- **System:** Key-Value based JSON files.
- **Workflow:** UI components must only store a `StringKey`. The `LocalizationSystem` replaces it with the actual text at runtime based on the selected language.
- **Moddable:** New languages can be added by placing a new JSON file in the `StreamingAssets/Languages` folder.