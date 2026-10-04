# MapleRide / MapleCycling

MapleRide is a Unity 6 HDRP cycling game. This repository contains the Unity
project, including scenes, game code, models, textures, audio and project settings.

## Open the game

1. Install Unity **6000.4.11f1** through Unity Hub.
2. Install Git LFS, then clone this repository:

   ```sh
   git lfs install
   git clone https://github.com/jasonsouryamath/MapleCycling.git
   cd MapleCycling
   git lfs pull
   ```

3. Add the cloned folder in Unity Hub and open it. Allow Unity to import the
   assets and resolve the packages in `Packages/manifest.json`.
4. Open `Assets/Scenes/MapleRideBoot.unity` and enter Play mode.

Use the Unity version recorded in `ProjectSettings/ProjectVersion.txt`.
Large assets use Git LFS; cloning without their contents will not produce a
complete playable project.

## Project layout

- `Assets/`: runtime/editor code, playable scenes and game assets.
- `Packages/`: Unity package manifest and lock file.
- `ProjectSettings/`: checked-in Unity project settings.
- `tools/`: project authoring and validation scripts. Some existing workstation
  helpers contain local paths and require configuration on a different computer.
- `MAPLERIDE_PROJECT_CONTEXT.md`: project design context.

Unity caches, machine settings, logs, backups and bundled Blender installations
are excluded. Unity regenerates its caches when the project is opened.
