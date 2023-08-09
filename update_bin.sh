#!/usr/bin/env bash

set -e

cd ./dependencies

function copyFile () {
    cp -v "$2" "$1"
}

export -f copyFile

for file in *; do
    if [ -f "$file" ]; then
        find -L ./source -name "$file" -exec bash -c "copyFile \"$file\" \"\$@\"" bash {} +
    fi
done

