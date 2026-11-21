# Native Windows against Docker

Run on 2026-11-20 with `tools/bench.cs`, once against the pinned image and once with `--native`, which uses the Windows release of the same build. The sweep is `sweeps/native-vs-docker.csv`, the rows are in `results/native-vs-docker.csv`, and the raw JSON, logs and GPU samples are in `results/raw/`, named after the run id.

## What was compared

|          | Docker                                  | Native                                         |
| -------- | --------------------------------------- | ---------------------------------------------- |
| Build    | 12030, commit `a91eab67d`               | 12030, commit `a91eab67d`                      |
| Binaries | the pinned image in `docker/images.md`  | `llama-b12030-bin-win-cuda-12.4-x64.zip`       |
| CUDA     | 12.8.1                                  | 12.4                                           |
| Compiler | GCC 14.2.0, for Linux                   | Clang 20.1.8, for Windows                      |
| Runs in  | Docker Desktop, on WSL2                 | Windows, directly                              |
| Models   | the `llm-models` volume                 | a copy on the Windows disk, same SHA-256       |

The details of the native build are in `docker/images.md`. Same driver (617.42) for both.

Two configurations, both with flash attention on, f16 KV cache, 10 threads, and 5 repetitions of pp512 and tg128:

- Qwen3-8B Q4_K_M with every layer on the GPU.
- Qwen3-14B Q4_K_M at `-ngl 32` (31 of 40 layers plus output), so part of it runs on the CPU.

Docker and native took turns: Docker, native, Docker, native. The GPU cooled to 45 °C before each configuration.

## Results

Tokens per second, mean of 5 repetitions.

| Model | Test  | Docker 1 | Docker 2 | Native 1 | Native 2 | Native over Docker |
| ----- | ----- | -------: | -------: | -------: | -------: | -----------------: |
| 8B    | pp512 |     2753 |     2769 |     2772 |     2783 |             +0.6 % |
| 8B    | tg128 |     74.4 |     75.7 |     76.2 |     76.5 |             +1.7 % |
| 14B   | pp512 |      869 |      886 |      962 |      964 |             +9.8 % |
| 14B   | tg128 |    17.86 |    17.62 |    18.38 |    18.39 |             +3.6 % |

The last column compares the mean of the two native runs with the mean of the two Docker runs.

## What the results say

- With the whole model on the GPU, native and Docker are close: under 2 % apart. Docker's own two runs of 8B tg128 are 1.7 % apart, so that gap is about the size of the noise.
- With part of the model on the CPU, native is clearly faster: about 10 % on prompt processing and 4 % on generation for 14B at `-ngl 32`. Both native runs agree to within 0.2 %, and both are above both Docker runs.
- Peak VRAM, power and clocks are about the same in both. The rows have the numbers.
- The two builds differ in more than where they run: CUDA 12.4 against 12.8.1, and Clang against GCC. So this shows the native release is faster here, not why. It doesn't separate WSL2 from the rest.
- Only `llama-bench` was compared. Model loading time and `llama-server` weren't.
