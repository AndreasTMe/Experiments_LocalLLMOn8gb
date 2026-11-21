# 01: How much model fits in 8 GB

How big a model an RTX 3060 Ti (8 GB) can run, how fast, with how much context, and what's lost along the way. Qwen3 at 4B, 8B, 14B and 30B-A3B, from Q8_0 down to Q4_K_M, all in llama.cpp in Docker.

## Versions

Everything ran on the machine in `hardware/hardware.md`.

| What             | Version                                                                                                           |
| ---------------- | ----------------------------------------------------------------------------------------------------------------- |
| GPU              | NVIDIA GeForce RTX 3060 Ti, 8192 MiB, power limit 220 W                                                           |
| Driver           | 617.42, WDDM                                                                                                      |
| Windows          | Windows 10 Pro, build 19045                                                                                       |
| WSL              | 3.0.3.0, kernel 6.18.54.1                                                                                         |
| Docker engine    | 29.9.2 when the hardware was recorded, 29.9.3 at the end                                                          |
| llama.cpp        | build 12030, commit `a91eab67d8389d979d29fdf6633b2edd0718eb4b`, CUDA 12.8.1                                       |
| llama.cpp image  | `ghcr.io/ggml-org/llama.cpp@sha256:03d61e752ac2de8011535ef41c5feb07fb9765d435181c4545eec5c790136c05`              |
| Ollama image     | `ollama/ollama@sha256:910d0d8107b538fb0894e63163eb195de0cc7aefcbc666dc62d3b18f71124592` (0.40.16)                 |
| .NET SDK image   | `mcr.microsoft.com/dotnet/sdk@sha256:bef136a371963aaa46ec97882845ef6f54dec15728bb4b128b1c2a400ffbc9b4` (10.0.404) |
| .NET on the host | SDK 11.0.100, the newest installed; nothing pins it, and the tools need .NET 10 or newer                          |

`docker/images.md` has the details of each image, and of the native Windows build used for one comparison. The `--help` output that every flag was checked against is in `docker/help/`.

## Models

All from the official Qwen GGUF repos on Hugging Face, Apache-2.0. The files live in the `llm-models` Docker volume, never in the repo. `models/README.md` says how to download one and check it.

| File                        |   Size (bytes) | SHA-256                                                            |
| --------------------------- | -------------: | ------------------------------------------------------------------ |
| `Qwen3-4B-Q4_K_M.gguf`      |  2,497,280,256 | `7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5` |
| `Qwen3-4B-Q5_K_M.gguf`      |  2,889,513,184 | `aca596860e8cb40af6539e3f2ea40df305f42515deac56d49c08d39a02e6533f` |
| `Qwen3-4B-Q6_K.gguf`        |  3,306,260,704 | `8a08533841623c2a689763b9318d8e27b3e052ad5b38015d4be4c04fa96f68a3` |
| `Qwen3-4B-Q8_0.gguf`        |  4,280,404,704 | `8c2f07f26af9747e41988551106f149b03eb9b5cb6df636027b6bf6278473300` |
| `Qwen3-8B-Q4_K_M.gguf`      |  5,027,783,488 | `d98cdcbd03e17ce47681435b5150e34c1417f50b5c0019dd560e4882c5745785` |
| `Qwen3-8B-Q5_K_M.gguf`      |  5,851,112,224 | `068bae163faa96ad48032daf4e071a6a28fe67d8dcc95367609c2ff165e52738` |
| `Qwen3-8B-Q6_K.gguf`        |  6,725,899,040 | `cb042ccd76795a8830d6be6bd4165245847cc68e41797b13bd61aed4c2cfbce6` |
| `Qwen3-8B-Q8_0.gguf`        |  8,709,518,112 | `408b955510e196121c1c375201744783b5c9a43c7956d73fc78df54c66e883d6` |
| `Qwen3-14B-Q4_K_M.gguf`     |  9,001,752,960 | `500a8806e85ee9c83f3ae08420295592451379b4f8cf2d0f41c15dffeb6b81f0` |
| `Qwen3-30B-A3B-Q4_K_M.gguf` | 18,556,685,824 | `0d003f6662faee786ed5da3e31b29c978de5ae5d275c8794c606a7f3c01aa8f5` |

The same list, with the repo for each, is `models/models.csv`.

## Folders

| Folder / file  | Contents                                                                   |
| -------------- | -------------------------------------------------------------------------- |
| `estimates.md` | What I expected, written before any benchmark ran                          |
| `sweeps/`      | The configurations `tools/bench.cs` ran, one CSV per sweep                 |
| `results/`     | One CSV per measurement, plus `raw/` with every log, JSON output and reply |
| `notes/`       | What each measurement found, one file per topic                            |

## Commands

Run from the repo root. Each tool is described in `tools/README.md`. Every result row has a `run_id`, and the files in `results/raw/` are named after it.

Benchmarks, with `llama-bench`:

```powershell
dotnet run tools/ceiling.cs -- --experiment 01-how-much-fits
dotnet run tools/bench.cs -- --experiment 01-how-much-fits --sweep experiments/01-how-much-fits/sweeps/<sweep>.csv --out experiments/01-how-much-fits/results/<results>.csv
```

| Sweep                                                                            | Results                |
| -------------------------------------------------------------------------------- | ---------------------- |
| `repeat-check.csv`                                                               | `repeat-check.csv`     |
| `smoke.csv`                                                                      | `smoke.csv`            |
| `dense-full-gpu.csv`                                                             | `dense-full-gpu.csv`   |
| `thread-check.csv`                                                               | `thread-check.csv`     |
| `offload-cliff-8b-q80.csv`, `offload-cliff-14b-q4km.csv`                         | `offload-cliff.csv`    |
| `max-context-*.csv`                                                              | `max-context.csv`      |
| `context-depth.csv`                                                              | `context.csv`          |
| `moe-offload.csv`                                                                | `moe.csv`              |
| `native-vs-docker.csv`, with and without `--native staging/llama-b12030-win/bin` | `native-vs-docker.csv` |
| `ollama-reference.csv`                                                           | `ollama-reference.csv` |

Quantisation loss, task scores, long context and Ollama:

```powershell
dotnet run tools/kld.cs -- --experiment 01-how-much-fits
dotnet run tools/tasks.cs -- run --experiment 01-how-much-fits --model Qwen3-8B-Q4_K_M.gguf --cache-type-k q8_0 --cache-type-v q8_0
dotnet run tools/long-context.cs -- --ctx 32768 --out staging/long-context/long-context-32k.jsonl
dotnet run tools/tasks.cs -- run --experiment 01-how-much-fits --model Qwen3-8B-Q4_K_M.gguf --ctx 32768 --cache-type-k q8_0 --cache-type-v q8_0 --tasks staging/long-context/long-context-32k.jsonl --out experiments/01-how-much-fits/results/long-context.csv
dotnet run tools/ollama.cs -- --experiment 01-how-much-fits --models <imported models>
```

The settings of every task run are in the columns of `results/tasks.csv` and `results/long-context.csv`, so each row can be run again with the matching flags.

## Reproducing one result

Smoke result for Qwen3-8B Q4_K_M, every layer on the GPU. Close everything that uses the GPU first, then run:

```powershell
docker run --rm --gpus all -v llm-models:/models:ro --entrypoint /app/llama-bench ghcr.io/ggml-org/llama.cpp@sha256:03d61e752ac2de8011535ef41c5feb07fb9765d435181c4545eec5c790136c05 -m /models/Qwen3-8B-Q4_K_M.gguf -ngl 99 -fa on -ctk f16 -ctv f16 -t 10 -p 512 -n 512 -r 5
```

In `results/smoke.csv`, three runs of this gave tg512 of 71.9, 72.1 and 73.7 tokens per second, and pp512 of 2617–2725. The noise threshold for this experiment is 3 % on tg, so a tg512 between 70 and 76 matches on this card.

Run again from this README on 2026-11-21: tg512 75.50 ± 0.09 and pp512 2770 ± 28. That's inside the range, but at its fast end: 2.5 % above the fastest smoke run and 4 % above their mean. The Docker runs of 8B in `results/native-vs-docker.csv`, from 2026-11-20, were also faster than the smoke runs, at 74.4–75.7 for tg128. Same driver and image, so I don't know why.

## Three surprises

1. **Much more context fits than I expected.** I estimated about 12k tokens for the 8B Q4_K_M with an f16 KV cache, and 24k with q8_0. It ran at full speed up to about 21k with f16, 34k with q8_0, and the full 40,960 with q4_0, and found a planted fact at every length tested (`notes/long-context.md`).
2. **Ollama's own choice costs 14B a third of its speed.** It leaves the split to llama-server's fit step, which keeps 1 GB free and stops at 27 layers. `-ngl 32` fits on the same card and is 50 % faster (`notes/ollama.md`).
3. **Docker costs nothing until the CPU joins in.** With the whole model on the GPU, Docker and the native Windows build are within 2 %. With part of 14B on the CPU, native is about 10 % faster at reading the prompt and 4 % at generating (`notes/native-vs-docker.md`).

## Notes

| Note                          | What it covers                                          |
| ----------------------------- | ------------------------------------------------------- |
| `notes/baseline.md`           | How much VRAM and RAM is really free                    |
| `notes/kv-cache-check.md`     | Which KV cache types the image runs, and at what speed  |
| `notes/shortlist.md`          | Task scores for each quant and configuration            |
| `notes/long-context.md`       | Finding a fact in a long text, up to 39k tokens         |
| `notes/native-vs-docker.md`   | Native Windows against Docker                           |
| `notes/ollama.md`             | Ollama with its defaults against llama.cpp              |
| `notes/energy-and-payback.md` | Energy per token, API payback, and what to run for what |
