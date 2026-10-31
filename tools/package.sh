#!/bin/sh
# Builds the stand-alone game for Windows and Mac into build/, as two zips
# ready to hand out. Needs Godot 4.6.1 .NET and its export templates
# (Godot: Editor > Manage Export Templates > Download and Install).
#
#     GODOT=/path/to/godot sh tools/package.sh
#
# Each zip holds the game and a copy of the data folder beside it, so the
# numbers can still be changed with a text editor.
set -e
cd "$(dirname "$0")/.."
GODOT=${GODOT:-godot}
NAME="Close Combat Saltdal"
rm -rf build
mkdir -p build/windows build/mac "build/$NAME (Windows)" "build/$NAME (Mac)"

"$GODOT" --headless --path . --export-release "Windows" build/windows/CloseCombatSaltdal.exe
"$GODOT" --headless --path . --export-release "Mac" build/mac/CloseCombatSaltdal.zip

copy_data() {
    mkdir -p "$1/data/battles"
    cp data/weapons.json data/units.json "$1/data/"
    cp data/battles/*.json "$1/data/battles/"
}

W="build/$NAME (Windows)"
cp -r build/windows/* "$W/"
copy_data "$W"
cp tools/readme_windows.txt "$W/Read me.txt"
(cd build && zip -qr -9 "CloseCombatSaltdal-windows.zip" "$NAME (Windows)")

M="build/$NAME (Mac)"
(cd "$M" && unzip -q ../mac/CloseCombatSaltdal.zip)
copy_data "$M"
cp tools/readme_mac.txt "$M/Read me.txt"
(cd build && zip -qry -9 "CloseCombatSaltdal-mac.zip" "$NAME (Mac)")

ls -la build/*.zip
