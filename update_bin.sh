#!/usr/bin/env bash

# ./dependencies/source expected to be symlink to main spt path

set -e

cd ./dependencies

function copyFile () {
    cp -v "$2" "$1"
}

export -f copyFile

for file in *; do
    if [ -f "$file" ]; then
        # most readable bash code
        find -L ./source -name "$file" -exec bash -c "copyFile \"$file\" \"\$@\"" bash {} +
    fi
done

