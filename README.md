# VP Physics Hands

Unity 6000.4.7f1 / URP / Metal application for physical hands and Gaussian scene interaction.

- Restore the local Gaussian assets described below, then open **apps/unity** in Unity Hub. The application scene is `Assets/Scenes/GaussianWorld.unity`.
- Runtime modules live under `Assets/Modules` with explicit assembly references. Content, scenes, prefabs and project settings are separate asset roots. SDK-managed configuration stays at the package-required `Assets/Resources` (PolySpatial) and `Assets/XR` paths.
- Export through **Physics Hands > Build > visionOS Xcode Project**. Output: `artifacts/builds/visionOS`. After the model assets are restored, a normal build uses the authored Unity assets and requires neither Python nor raw datasets.
- Install offline dependencies with `python3 -m pip install -r tools/requirements.txt`. Each tool supports `--help` and resolves default inputs relative to the repository, independently of the working directory.
- Place original datasets in `data/raw/flower` and `data/raw/physflow/<scene>`; PhysFlow configurations belong in `data/raw/physflow/configs`. Prepared payloads are written to the corresponding directories in `data/processed`.
- Scene scale and preprocessing resolution settings are in `tools/preprocessing/scene_scales.json`. Changing them requires preprocessing and reimporting the corresponding model.
- Import prepared PhysFlow assets through **Physics Hands > Gaussian World > Reimport PhysFlow Assets**. The learned flower importer uses `Assets/Settings/FlowerBakeSettings.asset`, including its explicit target, cage parameters, mode count and Python executable. Open scenes are not used as import configuration.
- `training` is reserved for future external training work; no training framework is included.
- Build and verification artifacts are ignored under `artifacts`. Runtime CSV metrics use the platform persistent data directory. Raw datasets, virtual environments and Unity-generated state are not versioned.

Large Gaussian model payloads are intentionally excluded from Git: `Assets/Content/Gaussians/*.asset` and the `PhysFlow` model directory, including their matching metadata. Before opening a fresh clone in Unity, restore the full `apps/unity/Assets/Content/Gaussians` directory from an existing local project or a separately supplied model archive. Preserve paths and all `.meta` files: the authored scene, render bindings and bake settings reference their GUIDs. Small shared materials remain versioned.

A fresh clone contains the code and configuration but cannot run or build `GaussianWorld` until these model assets are restored. This repository does not host a model archive, and `git lfs pull` does not retrieve the excluded payloads. Alternatively, regenerate assets using the preprocessing and import commands above, then reassign any newly generated model and render assets in the scene and flower bake settings; regenerated GUIDs may differ.

Existing research and measurements remain under `research`; temporary migration and regression tools are not part of the final project.
