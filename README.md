# Racing Woohoo RL

Unity racing project prepared for a Gymnasium reinforcement-learning integration.

## Repository scope

- `racing game (unity)/Assets/Scripts/`: vehicle and gameplay source code
- `racing game (unity)/Assets/Scenes/main.unity`: primary showcase scene and its direct asset dependencies
- `racing game (unity)/Packages/`: Unity package manifest and lock file
- `racing game (unity)/ProjectSettings/`: settings required to open the project
- `python/`: reserved for the Gymnasium environment, training scripts, and evaluation tools

Generated Unity data, IDE files, training outputs, and unrelated test or vendor assets are intentionally excluded.

## Planned RL workflow

1. Add a Unity agent/environment bridge under `racing game (unity)/Assets/Scripts/`.
2. Add the Gymnasium environment and training entry points under `python/`.
3. Store reproducible dependency declarations and evaluation instructions with the training code.
