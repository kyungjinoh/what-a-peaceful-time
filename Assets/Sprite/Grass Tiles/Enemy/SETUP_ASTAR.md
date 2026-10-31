# A* Pathfinding Setup for Enemy AI

The **EnemyAI** script now uses the A* Pathfinding Project. Enemies will pathfind around obstacles (Sea, collision-low, etc.) to reach the player.

---

## 2. What you need in the scene

A* needs a graph in the scene. Follow these steps in order.

### 2.1 Add AstarPath

1. Open the scene that has your **Grid** and **Enemy_Torch** (e.g. **SampleScene**).
2. In the Hierarchy: **GameObject → Create Empty**.
3. Rename it (e.g. **AstarPath** or **Pathfinding**).
4. With that object selected, in the Inspector click **Add Component**.
5. Search for **AstarPath** or go to **Pathfinding → AstarPath** (from the A* Pathfinding Project).
6. Add it. You should see the **AstarPath** component with a **Graphs** list (empty at first).

### 2.2 Add a Grid Graph

1. Keep the same GameObject selected (the one with **AstarPath**).
2. In the **AstarPath** component, find the **Graphs** section.
3. Click the **Add Graph** button.
4. In the menu, choose **Grid Graph**.
5. A new **Grid Graph** entry appears and its settings expand in the Inspector. You’ll use these for size, collision, and 2D.

### 2.3 Set it up for 2D

**Important:** If you skip this or get the rotation wrong, the graph will be in the XZ plane and the enemy will pathfind to the **wrong place** (e.g. wrong Y or a fixed spot). For 2D (XY) games this step is required.

1. **Graph name (optional)**  
   At the top of the Grid Graph you can set a name (e.g. “Main 2D Grid”).

2. **Rotation (so the graph is in the XY plane)**  
   - Find **Rotation** (under the graph’s position/size area).  
   - For a typical Unity 2D game (Y up, movement in XY): set **X = 90** (and Y/Z = 0).  
   - Alternatively, if your A* version has an **Is 2D** checkbox on the Grid Graph, enable it (it sets the rotation for you).
   - This makes the graph lie in the XY plane so nodes match your 2D world.

3. **Collision Testing (2D and layers)**  
   - Open the **Collision Testing** section.  
   - Enable **Use 2D** (checkbox).  
   - Set **Collision type** to **2D** (e.g. **Circle** with a small **Diameter** is fine for 2D).  
   - **Obstacle Layer Mask**: click the dropdown — **do not leave it on "Nothing"**.  
     - Add **Sea** and **collision-low** (and any other layers that are walls/water/obstacles).  
     - If this stays **Nothing**, every node is walkable and enemies will path **through** sea and walls.  
   - Nodes that overlap colliders on these layers will be marked unwalkable, so enemies path around them.

4. **Height Testing (optional for flat 2D)**  
   If your level is flat 2D, you can leave height testing off or use the defaults. If you use 3D colliders later, you can adjust this.

### 2.4 Position and size the graph

1. **Center**  
   Set **Center** X/Y (and Z if needed) so the graph is over your play area (e.g. same as your Grid/tilemap center).

2. **Width & Depth**  
   - **Width** and **Depth** are the number of nodes along each axis.  
   - Increase them until the graph is larger than your playable area (e.g. 100×100 or match your tilemap size in tiles).

3. **Node Size**  
   - **Node Size** is the distance between nodes in world units.  
   - Match your tile size if you use a tilemap (e.g. 1 if one tile = 1 unit).  
   - Smaller = more accurate but more nodes and a heavier graph.

4. **Preview**  
   In the Scene view you should see a grid overlay. Adjust Center, Width, Depth, and Node Size until it covers the area where the Player and enemies move.

### 2.5 Scan

1. With the Grid Graph still selected/expanded, find the **Scan** button (usually at the bottom of the Grid Graph section or in the main AstarPath toolbar).
2. Click **Scan**.  
   - A* will build the graph: walkable nodes where there’s no collision, unwalkable where Sea/collision-low (and other masked layers) are.
3. In the Scene view you should see the graph update (walkable nodes visible, unwalkable typically not or shown differently).
4. **After changing collision layers or level geometry, click Scan again** so the graph stays correct.

---

After this, enemies using **EnemyAI** will pathfind around Sea and collision-low instead of the old raycast logic.

## 3. Enemy setup

**Enemy_Torch** (or any enemy with **EnemyAI**):

- **EnemyAI** is already on the enemy. It will add **Seeker**, **AIPath**, and **AIDestinationSetter** at runtime if they are missing.
- The script finds the **Player** by tag and sets it as the A* target.
- No extra components are required; ensure the scene has an object tagged **Player**.

## 4. Optional

- **Speed**, **Stop Distance**, and **Repath Rate** are on **EnemyAI** in the Inspector.
- **EnemyCombat** is unchanged and still applies damage on collision.

## Troubleshooting

- **Enemy doesn’t move**: Ensure the scene has an **AstarPath** with at least one graph and that you clicked **Scan**. Check that the graph covers the enemy and player positions.
- **Enemy only moves in one direction (e.g. only up)**: The enemy now uses **AILerp** and the **Rigidbody2D** (if any) is set to **Kinematic** with only **Freeze Rotation**. Remove any **Missing (Script)** component from the enemy (fixes "The referenced script (Unknown) on this Behaviour is missing!").
- **Enemy walks through obstacles**: Set the Grid Graph’s **Obstacle Layer Mask** (under Collision Testing) to include **Sea** and **collision-low** — not **Nothing**. Then click **Scan** again.
- **Full docs**: [A* Pathfinding Project](https://arongranberg.com/astar/docs/getstarted.html)
