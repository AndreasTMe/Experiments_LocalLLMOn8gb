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
