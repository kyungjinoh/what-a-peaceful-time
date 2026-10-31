# What a Peaceful Town

A 2D top-down survival game set in a quiet town that is not as peaceful as it looks. Survive as long as you can while enemies hunt you with pathfinding AI, melee attacks, teleports, and other special moves.

## Requirements

- **Unity** `6000.2.0f1` (Unity 6)
- Built with the **2D** feature set, **Input System**, **UGUI**, and **TextMesh Pro**
- Uses **[A\* Pathfinding Project](https://arongranberg.com/astar/)** for enemy navigation around obstacles

## How to open

1. Clone this repository.
2. Open the project folder in Unity Hub (Unity 6 / `6000.2.0f1` recommended).
3. Open `Assets/Scenes/SampleScene.unity` and press Play.

## Gameplay

- Move with the keyboard (horizontal / vertical axes).
- Enemies chase the player using A\* grid pathfinding and can attack in melee.
- Survive as long as possible — a survival timer tracks how long you last.
- Player health regenerates after a short delay when you stop taking damage.
- On death, a replay / restart flow lets you try again.

## Project layout

```
Assets/
  Scenes/                 Main play scene
  Scripts/                Player movement
  Sprite/                 Art, tiles, and enemy gameplay scripts
  AstarPathfindingProject/ Enemy pathfinding package
  Resources/Audio/        Game audio
  TextMesh Pro/           UI text
```

Key gameplay scripts live under `Assets/Sprite/Grass Tiles/Enemy/` (movement, combat, health, UI, spawners, vision limiter, and special moves).

## Notes

- Generated Unity folders (`Library/`, `Temp/`, `Logs/`, `UserSettings/`) are not committed. Unity recreates them when you open the project.
- Local build outputs (for example `*.app`) are ignored.
