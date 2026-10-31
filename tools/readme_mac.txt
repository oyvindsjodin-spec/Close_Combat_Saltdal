Close Combat Saltdal
====================

A real-time tactics game set in Saltdal, Norway, in May 1940. Runs on Macs
with Apple silicon and on Intel Macs, macOS 11 or newer.

To play: open "Close Combat Saltdal.app". Keep the app and the data folder
together in the same folder. Nothing needs installing.

The game is not signed by Apple, so the first time macOS stops it:
  1. Double-click the app. macOS says it cannot be opened. Click Done.
  2. Open System Settings > Privacy & Security, scroll down, and click
     "Open Anyway" next to Close Combat Saltdal. Confirm with your password.
After that it opens normally.

If macOS instead says the app "is damaged", open Terminal, type
    xattr -cr 
(with a space at the end), drag the app onto the Terminal window, and press
Return. Then open the app again.

New to the game? Press the button at the top of the menu for a practice
battle that teaches the controls.

Keys during a battle: Space pause, F aim and fire, Tab next team, 1/2/3 game
speed, F5 save the battle, F3 sound on or off, C twice ask for a ceasefire.

The numbers of the game (weapons, teams, battles) are in the data folder.
Change them with TextEdit and start a new battle to try them. To undo a
change, copy the data folder back from the zip.

Saved games are kept in ~/Library/Application Support/Godot/app_userdata/Close Combat Saltdal.
