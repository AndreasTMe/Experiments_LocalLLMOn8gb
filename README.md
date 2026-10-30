# Local LLMs on 8 GB

Experiments running local models on an RTX 3060 Ti (8 GB) on Windows 10, with inference in Docker Desktop (WSL2 backend).

Each experiment has its own folder under `experiments/` and gets a git tag when its post is published. The shared parts live at the root:

| Folder      | Contents                                                  |
| ----------- | --------------------------------------------------------- |
| `hardware/` | The machine, recorded once                                |
| `docker/`   | Pinned images (by digest) and saved `--help` output       |
| `models/`   | Model manifest: repo, file name, SHA-256. No model files. |
| `tools/`    | .NET 10 file-based apps (`dotnet run tools/x.cs`)         |
| `tasks/`    | Quality task set and its format                           |

## Experiments

| #   | Question                    | Folder                          | Tag     |
| --- | --------------------------- | ------------------------------- | ------- |
| 01  | How much model fits in 8 GB | `experiments/01-how-much-fits/` | not yet |

The reproducible `docker run` command will be added when experiment 01 is frozen.
