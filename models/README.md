# Models

`models.csv` lists every model file: repo, name, size and SHA-256. The files themselves live in the `llm-models` Docker volume, never in the repo. Inference always reads them from that volume through a read-only mount, never from a Windows path.

## Adding a model

Run these from the repo root in PowerShell. Replace the repo, file name and SHA-256 with the values from `models.csv`.

1. Download into `staging/`, which git ignores:

```powershell
curl.exe -L --fail --create-dirs -C - -o staging/Qwen3-4B-Q4_K_M.gguf https://huggingface.co/Qwen/Qwen3-4B-GGUF/resolve/main/Qwen3-4B-Q4_K_M.gguf
```

2. Copy it into the volume and verify the SHA-256 there, using the pinned image from `docker/images.md`:

```powershell
docker run --rm --entrypoint /bin/sh -v llm-models:/models -v "${PWD}/staging:/staging:ro" ghcr.io/ggml-org/llama.cpp@sha256:03d61e752ac2de8011535ef41c5feb07fb9765d435181c4545eec5c790136c05 -c 'cp /staging/Qwen3-4B-Q4_K_M.gguf /models/ && echo 7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5\ \ /models/Qwen3-4B-Q4_K_M.gguf | sha256sum -c'
```

It prints `OK` when the hash matches.

3. Delete the file from `staging/` once it says `OK`.

## What's in the volume

Checked 2026-11-12: all 10 files in `models.csv` are in the volume, and `sha256sum -c` inside the pinned image says `OK` for each.

| What                                              | Bytes           | About   |
| ------------------------------------------------- | --------------- | ------- |
| Model files in the volume                         | 66,846,210,496  | 62 GiB  |
| Free on L:, which holds Docker Desktop's WSL disk | 852,993,863,680 | 794 GiB |

To check the hashes again:

```bash
tail -n +2 models/models.csv | awk -F, '{print $6"  /models/"$3}' | docker run --rm -i --pull never --entrypoint /bin/sh -v llm-models:/models:ro <image from docker/images.md> -c 'sha256sum -c'
```

## Using a model

Mount the volume read-only:

```
-v llm-models:/models:ro
```
