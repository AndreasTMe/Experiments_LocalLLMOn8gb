# Ollama with its defaults

Run on 2026-11-21 with `tools/ollama.cs`, against the pinned Ollama image in `docker/images.md`. The rows are in `results/ollama.csv`, and the llama-server log and replies of each load are in `results/raw/`, named after the run id. The llama.cpp reference is `sweeps/ollama-reference.csv`, run with `tools/bench.cs` into `results/ollama-reference.csv`.

## What was compared

The four Qwen3 Q4_K_M files: 4B, 8B, 14B and 30B-A3B. Ollama gets the same GGUF files, imported with a Modelfile that has only a `FROM` line, so there are no parameters or template changes of my own. The blob SHA-256 of each import matches the file in `models/models.csv`.

Ollama is left to choose everything. Each request sets only `num_predict`. Each model gets a 16-token warm-up, then 5 requests of 128 tokens with the same short prompt. The speed is Ollama's own `eval_count / eval_duration`.

llama.cpp is `llama-bench` tg128 with 5 repetitions, flash attention on, f16 KV cache and 10 threads. Each model runs with the best split found earlier. For 14B and 30B-A3B it also runs with the split Ollama picked.

The GPU cooled to 45 °C before each model, in both runs.

## What Ollama picked

Ollama 0.40.16 runs a llama-server it ships itself, build `6c01da627`, with its CUDA 13 backend. The image's llama.cpp is build 12030 with CUDA 12.8.1. For every model the command was the same:

```
-c 4096 -np 1 --flash-attn auto -b 512 -ub 512 --context-shift --keep 4
```

It passes no `-ngl` and no `--n-cpu-moe`. llama-server's fit step chooses the split, aiming to leave 1024 MiB of VRAM free.

| Model   | Context | Flash attention | KV cache | Threads | Split Ollama picked                         | Best llama.cpp split           |
| ------- | ------: | --------------- | -------- | ------: | ------------------------------------------- | ------------------------------ |
| 4B      |    4096 | on              | f16      |      10 | all 37 layers on the GPU                    | all layers on the GPU          |
| 8B      |    4096 | on              | f16      |      10 | all 37 layers on the GPU                    | all layers on the GPU          |
| 14B     |    4096 | on              | f16      |      10 | 27 of 41 on the GPU (26 plus output)        | `-ngl 32` (31 plus output)     |
| 30B-A3B |    4096 | on              | f16      |      10 | all 49 on the GPU, experts of 36 on the CPU | `--n-cpu-moe 33`               |

Ollama's layer counts include the output layer. The 4096 context is Ollama's default on this card. Thinking is on, and sampling uses Ollama's defaults. Neither affects the speed of a fixed 128 tokens.

## Results

Generation speed in tokens per second, as the mean of 5 runs.

| Model   | Ollama | llama.cpp, best split | Ollama against best | llama.cpp, Ollama's split |
| ------- | -----: | --------------------: | ------------------: | ------------------------: |
| 4B      |  114.3 |                 116.1 |              −1.5 % |                         — |
| 8B      |   71.7 |                  73.1 |              −1.9 % |                         — |
| 14B     |  11.59 |                 17.56 |               −34 % |                     11.69 |
| 30B-A3B |   27.1 |                  26.7 |              +1.5 % |                      28.5 |

VRAM in use after loading, for the whole GPU, from `nvidia-smi`. Ollama used 3791, 5899, 6837 and 6767 MiB. llama.cpp peaked at 3260, 5351, 7345 and 7065 MiB with the best splits, and 6393 and 6027 MiB with Ollama's.

## What the results say

- When the whole model fits, Ollama is within 2 % of llama.cpp, which is close to the noise. It also uses about 500 MiB more VRAM, for its 4096 context and its own buffers.
- For 14B, Ollama is a third slower. That comes from the split and not from Ollama itself: llama.cpp with Ollama's split, 27 layers, is just as slow. Fit stops at 27 layers to keep 1 GB free. `-ngl 32` fits in the same card and is 50 % faster.
- For 30B-A3B, the three results are within the noise of each other. All three runs, Ollama's included, start fast and slow down over the five repetitions. For example, the llama.cpp run with Ollama's split went from 34.4 to 25.5. So these numbers can't rank the splits. Fit's choice, 36 layers' experts on the CPU, isn't clearly worse than 33.
- The comparison isn't fully like for like. The llama.cpp builds differ, and so do the CUDA versions. Ollama times a request with an 18-token prompt in front, and `llama-bench` times generation from an empty context. Prompt processing and load times weren't compared.
