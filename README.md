# Close Combat Saltdal

A personal, non-commercial real-time tactics game in the style of the
Close Combat series (1996 to 1999), set in Saltdal, Norway, in May 1940.
The plan is in the project's development plan document.

**Status:** Phase 0 (foundations). One test map of the Pothus area built from
real elevation data, and a few placeholder teams you can move around.

## Running the game

You need the **Godot 4 .NET edition** (4.6 or newer) and the **.NET 8 SDK**.

1. Start Godot. In the Project Manager, click **Import**.
2. Pick the `project.godot` file in this folder, then **Import & Edit**.
3. The first time, click **Build** (the hammer icon, top right) and wait for it to finish.
4. Press **F5**, or the play button (top right), to run the game.

## Controls

| Action | Control |
| --- | --- |
| Select a team | Left-click one of its soldiers |
| Give an order | Right-click on the map, then pick Move, Move Fast, Sneak or Stop |
| Pan | W A S D, arrow keys, or drag with the middle mouse button |
| Zoom | Mouse wheel |
| Pause | Space |
| Deselect | Esc |

Both sides can be ordered in Phase 0, to test movement. Khaki dots are British,
grey dots are German.

## Layout

| Folder | What it holds |
| --- | --- |
| `src/Sim` | The simulation: map, pathfinding, soldiers, teams. Pure C#, no Godot code |
| `src/Game` | Godot scripts that draw the simulation and handle input |
| `scenes` | Godot scenes |
| `data/maps` | Battle maps (height, terrain type, metadata) |
| `tools` | `build_map.py`, which makes a map from real elevation data |
| `tests` | Unit tests for the simulation (`dotnet test tests/Saltdal.Sim.Tests`) |

## Maps

`tools/build_map.py` downloads open elevation tiles for a square area and
guesses terrain types (forest, rock, snow, river, road) from height and slope.
The Pothus map was built with:

    python3 tools/build_map.py pothus --lat 66.950 --lon 15.302

The location is a best guess at the Pothus battlefield and should be checked
against a map. Forest, fields, farms and the real road and bridges will be
painted by hand later.
