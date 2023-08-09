#!/usr/bin/env bash
set -e

if ! [[ -d "./dependencies/source" ]]; then
    echo "Expected ./dependencies/source to be a directory!"
    echo "source should be a symlink to your spt directory."
    exit 1;
fi


cd ./dependencies

if [[ "$(ls | wc -l)" == "1" ]]; then
    # pre-populate dummy files
    function populateFile () {
        touch "$1.dll";
    }

    # List of required assemblies
    # ".dll" is automatically postfixed
    populateFile "0Harmony";
    populateFile "Aki.Common";
    populateFile "Aki.Reflection";
    populateFile "Assembly-CSharp";
    populateFile "BepInEx";
    populateFile "Comfort";
    populateFile "Newtonsoft.Json";
    populateFile "UnityEngine";
    populateFile "UnityEngine.AudioModule";
    populateFile "UnityEngine.CoreModule";
    populateFile "UnityEngine.InputLegacyModule";
    populateFile "UnityEngine.InputModule";
fi



function copyFile () {
    cp -v "$2" "$1"
}

export -f copyFile

for file in *; do
    if [[ -f "$file" ]]; then
        # most readable bash code
        find -L ./source -name "$file" -exec bash -c "copyFile \"$file\" \"\$@\"" bash {} +
    fi
done

