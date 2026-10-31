# Close Combat Saltdal

A personal, non-commercial real-time tactics game in the style of the
Close Combat series (1996 to 1999), set in Saltdal, Norway, in May 1940.
The plan is in the project's development plan document.

**Status:** Phase 2b. One battle at Pothus on 25 May 1940, playable from
either side. As the British you command a platoon of the Irish Guards and a
Norwegian mortar team holding three flags against five German squads. As the
Germans you attack with those squads, a mortar and the platoon HQ, against a
computer that holds its flags, reinforces and counter-attacks. Soldiers see,
shoot, take cover, get pinned, panic and run; whoever holds more flag value when
time runs out wins. Mortars, 25-pounder guns, German mountain guns and a Stuka
dive bomber add shellfire, and leaders keep their men steady.

## Running the game

You need the **Godot 4 .NET edition** (4.6 or newer) and the **.NET 8 SDK**.

1. Start Godot. In the Project Manager, click **Import**.
2. Pick the `project.godot` file in this folder, then **Import & Edit**.
3. The first time, click **Build** (the hammer icon, top right) and wait for it to finish.
4. Press **F5**, or the play button (top right), to run the game.
5. Pick a side in the menu.

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
| Fight again after the battle | R (same side) or M (back to the menu) |
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
| Call 25-pounders / mountain guns / Stuka | HQ teams only (Platoon HQ, Zugtrupp): the guns shell the point about 50 seconds later. The HQ must be able to see the spot, and there are only two missions |

Mortar teams fire on their own, now and then, at groups of three or more
enemies they can see, but always keep the last 6 bombs back. Give a mortar team
a Fire order and it drops a barrage of 6 bombs on that point, even where it
cannot see. The point must be at least 150 m away (the Norwegian 81 mm reaches
2 km). The team's button shows how many bombs are left, and the messages at the
bottom left say when a barrage is on the way or why it cannot fire. Mortars
cannot fire while moving.

Teams with a leader (Lt O'Neill, Sgt Gallagher, the section corporals) react
to orders at once and recover their nerve faster. A team whose leader is down
waits a few seconds before obeying. Messages such as "on the way, 50 seconds" and
"Aircraft overhead!" appear at the bottom left.

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
