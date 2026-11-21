# Images

Containers are always started by digest, not by tag.

## llama.cpp

| Field          | Value                                                                                                        |
| -------------- | ------------------------------------------------------------------------------------------------------------ |
| Image          | `ghcr.io/ggml-org/llama.cpp`                                                                                 |
| Tag            | `full-cuda12-b12030`                                                                                         |
| Digest         | `sha256:03d61e752ac2de8011535ef41c5feb07fb9765d435181c4545eec5c790136c05`                                    |
| Local image ID | `sha256:57100dcf5f6efe056155f6b30261f8a741febbc5f2b1b72e7da3c4fb48f4a968`                                    |
| Size           | 3.6 GB compressed, 6.6 GB on disk                                                                            |
| Build          | 12030                                                                                                        |
| Version        | 0.8.0-dev                                                                                                    |
| Commit         | `a91eab67d` (from `--version`), full hash `a91eab67d8389d979d29fdf6633b2edd0718eb4b` (from the image labels) |
| CUDA           | 12.8.1                                                                                                       |
| Pulled         | 2026-11-01                                                                                                   |

```
ghcr.io/ggml-org/llama.cpp@sha256:03d61e752ac2de8011535ef41c5feb07fb9765d435181c4545eec5c790136c05
```

The image needs a driver with CUDA 12.8 or newer. On driver 560.94 (CUDA 12.6), `--gpus all` fails before the container starts, because of the image's `NVIDIA_REQUIRE_CUDA` setting.

The `--help` output for `llama-bench`, `llama-server` and `llama-perplexity` is in `help/b12030/`. Flags are checked against those files.

## GPU check

Run on 2026-11-01 with driver 617.42:

```bash
docker run --rm --gpus all --entrypoint /app/llama-server ghcr.io/ggml-org/llama.cpp@sha256:03d61e752ac2de8011535ef41c5feb07fb9765d435181c4545eec5c790136c05 --list-devices
```

```
Available devices:
  CUDA0: NVIDIA GeForce RTX 3060 Ti (8191 MiB, 7126 MiB free)
```

`ggml_cuda_init` reports compute capability 8.6. `nvidia-smi` also works inside the container. VRAM is still measured with `nvidia-smi` on Windows.

## Native Windows build

Not an image: the same llama.cpp build as the image, as Windows binaries, to compare against Docker. Both zips are from the b12030 release on GitHub (`ggml-org/llama.cpp`), unpacked into one folder, `staging/llama-b12030-win/bin/`, which isn't in the repo.

| Field     | Value                                                                                                     |
| --------- | --------------------------------------------------------------------------------------------------------- |
| Binaries  | `llama-b12030-bin-win-cuda-12.4-x64.zip`, 279,952,590 bytes                                               |
| SHA-256   | `c1ff9b27ff160a706b5a17d58804ef94e699687147dbc2cb3b8395e4b8d4e136`                                        |
| CUDA DLLs | `cudart-llama-bin-win-cuda-12.4-x64.zip`, 391,443,627 bytes                                               |
| SHA-256   | `8c79a9b226de4b3cacfd1f83d24f962d0773be79f1e7b75c6af4ded7e32ae1d6`                                        |
| Build     | 12030                                                                                                     |
| Version   | 0.8.0-dev                                                                                                 |
| Commit    | `a91eab67d` (from `--version`), the same as the image                                                     |
| Compiler  | Clang 20.1.8                                                                                              |
| CUDA      | 12.4, against 12.8.1 in the image                                                                         |
| CPU code  | `ggml-cpu-haswell.dll`, picked at start-up                                                                |

`llama-bench.exe --help` is in `help/b12030-win-cuda-12.4/`.

## Ollama

Compared against llama.cpp with its own defaults, on the same GGUF files.

| Field          | Value                                                                     |
| -------------- | ------------------------------------------------------------------------- |
| Image          | `ollama/ollama`                                                           |
| Tag            | `0.40.16`                                                                 |
| Digest         | `sha256:910d0d8107b538fb0894e63163eb195de0cc7aefcbc666dc62d3b18f71124592` |
| Local image ID | `sha256:6c437ef9d364e1712bc7e2342b224369e3669c29d9373f1bbdd68f4601d58fd9` |
| Size           | 5.1 GB on disk                                                            |
| Version        | 0.40.16                                                                   |
| OS             | Ubuntu 24.04                                                              |
| Pulled         | 2026-11-20                                                                |

```
ollama/ollama@sha256:910d0d8107b538fb0894e63163eb195de0cc7aefcbc666dc62d3b18f71124592
```

Its models live in their own writable volume, `ollama-data`. `llm-models` is mounted read-only, as everywhere else. The `--help` output for `ollama`, `serve`, `create`, `ps` and `show` is in `help/ollama-0.40.16/`.

## .NET SDK

Builds and runs the code that models write, for the code tasks in `tasks/`. Nothing in it touches the GPU.

| Field          | Value                                                                     |
| -------------- | ------------------------------------------------------------------------- |
| Image          | `mcr.microsoft.com/dotnet/sdk`                                            |
| Tag            | `10.0`                                                                    |
| Digest         | `sha256:bef136a371963aaa46ec97882845ef6f54dec15728bb4b128b1c2a400ffbc9b4` |
| Local image ID | `sha256:60f22a1c6a3e598ac3feed6d9a75d4de4b859c239a2a0b3ed95b49f26b0306bf` |
| Size           | 0.9 GB on disk                                                            |
| SDK            | 10.0.404                                                                  |
| Runtime        | 10.0.15                                                                   |
| OS             | Ubuntu 24.04.5 LTS                                                        |
| Pulled         | 2026-11-19                                                                |

```
mcr.microsoft.com/dotnet/sdk@sha256:bef136a371963aaa46ec97882845ef6f54dec15728bb4b128b1c2a400ffbc9b4
```
