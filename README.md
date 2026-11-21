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

| #   | Question                    | Folder                          | Tag                |
| --- | --------------------------- | ------------------------------- | ------------------ |
| 01  | How much model fits in 8 GB | `experiments/01-how-much-fits/` | `01-how-much-fits` |

## Running it yourself

You need an NVIDIA card with a driver for CUDA 12.8 or newer, Docker with GPU support (Docker Desktop on WSL2 here), and about 17 GB of disk: 6.6 GB for the image, and 5 GB for the model twice until the copy in `staging/` is deleted. The commands are PowerShell, run from the repo root.

1. Download the model into `staging/`, which git ignores:

```powershell
curl.exe -L --fail --create-dirs -C - -o staging/Qwen3-8B-Q4_K_M.gguf https://huggingface.co/Qwen/Qwen3-8B-GGUF/resolve/main/Qwen3-8B-Q4_K_M.gguf
```

2. Copy it into a Docker volume and check its SHA-256 there. It prints `OK` when the hash matches:

```powershell
docker run --rm --entrypoint /bin/sh -v llm-models:/models -v "${PWD}/staging:/staging:ro" ghcr.io/ggml-org/llama.cpp@sha256:03d61e752ac2de8011535ef41c5feb07fb9765d435181c4545eec5c790136c05 -c 'cp /staging/Qwen3-8B-Q4_K_M.gguf /models/ && echo d98cdcbd03e17ce47681435b5150e34c1417f50b5c0019dd560e4882c5745785\ \ /models/Qwen3-8B-Q4_K_M.gguf | sha256sum -c'
```

3. Close anything that uses the GPU, then benchmark it, every layer on the GPU:

```powershell
docker run --rm --gpus all -v llm-models:/models:ro --entrypoint /app/llama-bench ghcr.io/ggml-org/llama.cpp@sha256:03d61e752ac2de8011535ef41c5feb07fb9765d435181c4545eec5c790136c05 -m /models/Qwen3-8B-Q4_K_M.gguf -ngl 99 -fa on -ctk f16 -ctv f16 -t 10 -p 512 -n 512 -r 5
```

On the RTX 3060 Ti here, this gives about 72 tokens per second for tg512 and about 2,600 for pp512. Other models, the versions and every other command are in `experiments/01-how-much-fits/README.md`.
