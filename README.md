# sptarkov-deadzone
An SPT mod which adds insurgency style deadzone/free aim.

# Building
- ## Copy DLLs
    ### Automated method (currently linux only)
    - symlink ./dependencies/source to point to your spt path.
        ```bash
        ln -s ${PATH_TO_SPT} ./dependencies/source
        ```
    run `update_bin.sh`.
    ### Manual method
    Find all required dlls in your SPT folder, and copy them to the ``dependencies`` folder.

    For a list of all required dlls, open ``update_bin.sh`` in a text editor and find:
    ```bash
    # List of required assemblies
    populateFile "0Harmony";
    populateFile "Aki.Common";
               ...
               ...
               ...
    ```

- ## Compile
    run `dotnet build`.

    output assembly will be saved to ``./bin/Debug/net472/DeadzoneMod.dll``.

# Installing
Copy ``DeadzoneMod.dll`` to ``$SPT/BepInEx/plugins/``.
