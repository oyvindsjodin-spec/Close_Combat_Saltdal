# Close Combat Saltdal

A personal, non-commercial real-time tactics game in the style of the
Close Combat series (1996 to 1999), set in Saltdal, Norway, in May 1940.
The plan is in the project's development plan document.

**Status:** Phase 1 (vertical slice). One battle at Pothus on 25 May 1940:
you command a platoon of the Irish Guards holding three flags, and the computer
attacks with five German squads. Soldiers see, shoot, take cover, get pinned,
panic and run; whoever holds more flag value when time runs out wins.

## Running the game

You need the **Godot 4 .NET edition** (4.6 or newer) and the **.NET 8 SDK**.

1. Start Godot. In the Project Manager, click **Import**.
2. Pick the `project.godot` file in this folder, then **Import & Edit**.
3. The first time, click **Build** (the hammer icon, top right) and wait for it to finish.
4. Press **F5**, or the play button (top right), to run the game.

## Controls

| Action | Control |
| --- | --- |
| Select a team | Left-click one of its soldiers, click its button at the bottom, or press Tab |
| Give an order | Right-click on the map, then pick an order |
| Pan | W A S D, arrow keys, or drag with the middle mouse button |
| Zoom | Mouse wheel |
| Pause | Space (you can still give orders) |
| Game speed | 1, 2 or 3 for normal, double and four times speed |
| Call a ceasefire | C twice |
| Fight again after the battle | R |
| Show all soldiers (for testing) | F2 |

| Order | What the team does |
| --- | --- |
| Move | Walks to the point, returning fire at close range |
| Move Fast | Runs to the point without stopping to shoot |
| Sneak | Creeps, crouched and hard to spot, slowly |
| Fire | Shoots at enemies near the point, or sprays the area to keep heads down |
| Defend | Lies down facing the point and fires at anything in range |
| Hide | Lies low and holds fire unless the enemy gets very close |
| Stop | Halts where it is |

Khaki dots are British, grey dots are German. The ring around each man shows
his state of mind: black is steady, yellow pinned, orange cowering, red
panicked and purple routed. A cross is a dead man. You only see German soldiers
while one of your men can see them; a faint circle marks where one was last seen.

## Layout

| Folder | What it holds |
| --- | --- |
| `src/Sim` | The simulation: map, pathfinding, sight, firing, morale, flags, AI, scenarios. Pure C#, no Godot code |
| `src/Game` | Godot scripts that draw the simulation and handle input |
| `scenes` | Godot scenes |
| `data/maps` | Battle maps (height, terrain type, metadata) |
| `tools` | `build_map.py`, which makes a map from real elevation data |
| `tests` | Unit tests for the simulation (`dotnet test tests/Saltdal.Sim.Tests`) |

## Maps

`tools/build_map.py` downloads open elevation tiles for a square area and
guesses terrain types (forest, rock, snow, river, road) from height and slope.
The Pothus map was built with:

    python3 tools/build_map.py pothus --lat 66.950 --lon 15.302 --bridges 0.40,0.80

The location is a best guess at the Pothus battlefield and should be checked
against a map. Forest, fields, farms and the real road and bridges will be
painted by hand later.
