# Close Combat Saltdal

A personal, non-commercial real-time tactics game in the style of the
Close Combat series (1996 to 1999), set in Saltdal, Norway, in May 1940.
The plan is in the project's development plan document.

**Status:** Phase 3. Three battles from the fighting in Saltdal, each
playable from either side on its own or one after another as an operation:

| Battle | Map | What happens |
| --- | --- | --- |
| Viskiskoia, 23 May 1940 | Open birch upland, 1.5 km square | Scots Guards and an Independent Company hold the road bridge |
| Pothus, 25 May 1940 | Where a side valley joins Saltdalselva, 2 km square | Irish Guards and Norwegian mortars hold the west bank, the ridge and Pothus farm; the road bridge is blown |
| Rognan, 26 May 1940 | Village and quay at the head of the fjord, 2 km square | A rearguard holds the quay while the last boats leave |

The computer attacks or defends, whichever side you do not pick. Soldiers see,
shoot, take cover, get pinned, panic and run; whoever holds more flag value when
time runs out wins. Mortars, 25-pounder guns, German mountain guns and a Stuka
add shellfire, and leaders keep their men steady. Houses give good cover and
block sight; fords let men wade the river slowly and in the open.

## Running the game

You need the **Godot 4 .NET edition** (4.6 or newer) and the **.NET 8 SDK**.

1. Start Godot. In the Project Manager, click **Import**.
2. Pick the `project.godot` file in this folder, then **Import & Edit**.
3. The first time, click **Build** (the hammer icon, top right) and wait for it to finish.
4. Press **F5**, or the play button (top right), to run the game.
5. Pick a battle and a side in the menu.
6. Choose your forces. The historical force is ticked to begin with; your
   budget is what it cost. Untick a team to free points for a reserve team or
   more off-map support, then press **Start battle**. The computer always
   brings the historical force.

## The operation

Below the battles in the menu, **The operation, 23 to 26 May** fights all
three battles in order, as the British or as the Germans:

- Men who come through a battle fight again if their team is in the next one,
  each a little more experienced. The dead, the badly wounded and the captured
  are gone. The same German company fights all three battles; the Irish Guards
  sections and HQ fight at Pothus and again at Rognan.
- Before each battle some losses are made good with green replacements: three
  in four for the Germans, who kept feeding in fresh troops, one in four for
  the British. A British team with nobody left is gone for good.
- A team short of men costs fewer points, so losses leave points over for
  reserves. The winner of a battle gets 15% more points for the next.
- After each battle press **N** for the next one. The operation is saved after
  every battle; **Continue the operation** in the menu picks it up again.
- Whoever wins more of the three battles wins the operation.

## Controls

| Action | Control |
| --- | --- |
| Select a team | Left-click one of its soldiers, click its card in the bar at the bottom, or press Tab |
| Jump to a place | Click or drag on the mini-map, bottom right |
| Give an order | Right-click on the map, then pick an order |
| Pan | W A S D, arrow keys, or drag with the middle mouse button |
| Zoom | Mouse wheel |
| Aim and fire | Pick Fire from the order menu, or press F. A line runs from the team to the mouse: pale where your men can see, red beyond the point where the view is blocked, with the range and how many men can see the spot. Left-click to fire, right-click or Esc to cancel |
| Pause | Space (you can still give orders) |
| Start the attack early | Enter, while the clock counts down to the attack |
| Game speed | 1, 2 or 3 for normal, double and four times speed |
| Ask for a ceasefire | C twice. The enemy agrees once half the time has gone, or at once if you are behind on flags |
| After the battle | R fight again (single battle), N next battle (operation), M back to the menu |
| Show all soldiers (for testing) | F2 |

| Order | What the team does |
| --- | --- |
| Move | Walks to the point, returning fire at close range |
| Move Fast | Runs to the point without stopping to shoot |
| Sneak | Creeps, crouched and hard to spot, slowly |
| Fire | Takes aim (see above), then shoots at enemies near the point, or sprays the area to keep heads down |
| Defend | Lies down facing the point and fires at anything in range |
| Hide | Lies low and holds fire unless the enemy gets very close |
| Dig In | Each man digs a trench where he stands, facing the point. Takes about a minute and a half on grass, fields, forest or snow (not on rock, roads or in houses); then the team defends from the trenches |
| Stop | Halts where it is |
| Call 25-pounders, mountain guns or Stuka | HQ teams only (Platoon HQ, Zugtrupp): the strike lands about a minute later. The HQ must be able to see the spot, and each has only one or two missions |

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

British soldiers wear khaki and flat Brodie helmets, Germans field grey and
Stahlhelms, Norwegians grey-green. A coloured dot over a man shows he is shaken:
yellow pinned, orange cowering, red panicked, purple routed. The bar at the
bottom shows a card for each team (a square per man, coloured the same way),
the men of the selected team, and a mini-map. You only see German soldiers
while one of your men can see them; a faint circle marks where one was last seen.

## Houses, fire and trenches

Each battle starts with a quiet spell before the attack: 5 minutes at
Viskiskoia and Rognan, 10 at Pothus. Nobody fires and no guns can be called.
The defender uses the time to get into position and dig in; the attacker waits
where he is (press Enter to start the attack at once). The clock at the top
left counts down to the attack, then shows the battle time left.

Order a team to Move into a house and the men spread through its rooms, one at
each window. You see the house with its roof off while your men are in it.
Men inside are hard to hit and can shoot out of the windows, but nobody can see
through a house from one side to the other.

Shells, mortar bombs and bombs can set a house on fire, and a burning house can
set the one next to it alight. Men inside get out as fast as they can, however
frightened. The fire burns about five minutes and leaves a burnt-out ruin,
which still gives some cover. Smoke drifts off downwind and hides whatever is
behind it.

A man in a trench is about four times harder to hit lying down, and shell
splinters mostly fly over him. The British computer side digs in by itself
before the attack. You see your own trenches always, and enemy ones once your
men have spotted them.

## Changing the numbers

The game's numbers are in plain text files in `data/`, so you can change them
with any text editor (Notepad, TextEdit) and start a new battle to try them:

| File | What it holds |
| --- | --- |
| `data/weapons.json` | Range, rate of fire, accuracy, ammunition and blast of every weapon and shell |
| `data/units.json` | What each kind of team carries, its experience and its cost in points; the off-map guns and the Stuka |
| `data/battles/*.json` | Each battle: time limit, minutes before the attack (`prepMinutes`), flags, where every team starts, soldiers' names, which teams are historical and which are reserves, and the budget |

Keep the commas, quotes and brackets as they are. If a file has a mistake,
the game says which file and where instead of starting.

## Layout

| Folder | What it holds |
| --- | --- |
| `src/Sim` | The simulation: map, pathfinding, sight, firing, morale, flags, AI, scenarios. Pure C#, no Godot code |
| `src/Game` | Godot scripts that draw the simulation and handle input |
| `scenes` | Godot scenes |
| `data` | Weapons, units and battles as editable text files (`weapons.json`, `units.json`, `battles/`), and the battle maps (`maps/`) |
| `tools` | `build_map.py`, which makes a map from real elevation data, and `paint_ground.py`, which paints its ground picture |
| `tests` | Unit tests for the simulation (`dotnet test tests/Saltdal.Sim.Tests`) |

## Maps

Each map has a recipe in `tools/maps/<name>.json`: its centre and size, and the
parts the height model cannot give, written by hand (the road's route, blown
bridges, fords, side streams, farms with their fields and houses, a town).
`tools/build_map.py` downloads open elevation tiles for the area, guesses
forest, rock, snow and the river from height and slope, adds the recipe's parts
and writes the map to `data/maps/<name>/`:

    python3 tools/build_map.py tools/maps/pothus.json tools/maps/viskiskoia.json tools/maps/rognan.json

`tools/paint_ground.py` then paints the overhead picture the game shows,
`data/maps/<name>/ground.jpg`, at 2 pixels per metre: fields with furrows,
single trees with shadows, roofs, roads, the river with its depth, and hill
shading lit from the north-west. Run it again after changing a map:

    python3 tools/paint_ground.py pothus viskiskoia rognan

Locations: Pothus farm 66.983 N 15.333 E (the map is centred on 66.979 N
15.325 E), Viskiskoia 66.75 N 15.483 E, Rognan 67.098 N 15.39 E. The roads,
farms and houses are laid out from period photos and maps, not surveyed.
