# KV cache check

Run on 2026-11-02 against the pinned image in `docker/images.md` (build 12030, commit `a91eab67d`), driver 617.42, with `Qwen3-4B-Q4_K_M.gguf` from the `llm-models` volume.

## Decision

**The prebuilt image is enough.** The sweep uses matching K and V types (f16, q8_0 and q4_0), and all three have compiled flash-attention kernels. Mixed K and V types also run, but on a slower path, so they stay out of the sweep. If the sweep ever needs mixed types, the image has to be built with `GGML_CUDA_FA_ALL_QUANTS`.

## Build info

`system_info` line printed by `llama-perplexity` when it loads the model:

```
CUDA : ARCHS = 500,610,700,750,800,860,890,900,1200 | USE_GRAPHS = 1 | FA_QUANTS = q4_0-q4_0,q8_0-q8_0,f16-f16,bf16-bf16 | CPU : SSE3 = 1 | SSSE3 = 1 | AVX = 1 | AVX2 = 1 | F16C = 1 | FMA = 1 | BMI2 = 1 | LLAMAFILE = 1 | OPENMP = 1 | REPACK = 1 |
```

- 860 (the 3060 Ti, compute capability 8.6) is in the list.
- Flash attention is compiled only for matching K and V pairs: q4_0, q8_0, f16 and bf16. The all-quants build flag is off.
- The CPU backend loaded is `libggml-cpu-haswell.so` (AVX2), not the Alder Lake one. This only matters for the CPU offload runs.

## Matrix

All runs: `-ngl 99 -fa on`, 3 repetitions at depth 0 and 2 at depth 8192. Tokens per second.

| K    | V    | pp256 | tg32  | pp512 @ 8k | tg32 @ 8k | Result |
| ---- | ---- | ----: | ----: | ---------: | --------: | ------ |
| f16  | f16  |  3895 | 104.4 |       2628 |      76.6 | works  |
| f16  | q8_0 |  3837 | 100.6 |       2720 |      60.9 | works, slower decode at depth |
| f16  | q4_0 |  3779 | 100.4 |       2566 |      59.2 | works, slower decode at depth |
| q8_0 | f16  |  3803 | 100.2 |       2613 |      59.7 | works, slower decode at depth |
| q8_0 | q8_0 |  3756 | 100.0 |       2522 |      67.2 | works  |
| q8_0 | q4_0 |  3687 |  96.4 |       2580 |      51.8 | works, slower decode at depth |
| q4_0 | f16  |  3726 |  99.0 |       2594 |      58.8 | works, slower decode at depth |
| q4_0 | q8_0 |  3728 |  99.2 |       2565 |      50.9 | works, slower decode at depth |
| q4_0 | q4_0 |  3848 | 101.4 |       2542 |      66.2 | works  |

- No combination errored, and none collapsed to CPU speed (that would be single-digit decode).
- At depth 0 all nine are within about 8% of each other, because attention is a small part of the work there.
- At 8k depth, the mixed pairs decode 10–25% slower than the matched quantised pairs, and are slower than f16/f16. That's the cost of the missing kernels. Prefill is barely affected.
- The 8k rows came from one `llama-bench` run with comma-separated `-ctk` and `-ctv` lists. Between type changes it printed `state_read_data: ... failed to restore kv cache` and refilled the context instead of reusing it. Running q8_0/q4_0 and q8_0/q8_0 on their own gave the same numbers (51.4 and 67.2 decode) with no warning. The sweeps use one KV type per run anyway.

## Flags for the sweeps

Checked against `docker/help/b12030/`. Every flag below is in all three tools unless stated.

| What                  | Flag                                           | Notes |
| --------------------- | ---------------------------------------------- | ----- |
| GPU layers            | `-ngl`                                         | `llama-bench` default is -1. |
| Flash attention       | `-fa on`                                       | Default is `auto`; set it explicitly. |
| K cache type          | `-ctk`                                         | Default f16. |
| V cache type          | `-ctv`                                         | Default f16. |
| Context depth         | `-d`                                           | `llama-bench` only. |
| Context size          | `-c`                                           | `llama-server` and `llama-perplexity`. |
| Prompt / decode sizes | `-p`, `-n`                                     | `llama-bench`. |
| Repetitions           | `-r`                                           | `llama-bench`, default 5. |
| Output format         | `-o csv` (also `json`, `jsonl`, `md`, `sql`)   | `llama-bench`. |
| MoE experts on CPU    | `-ncmoe` / `--n-cpu-moe`                       | First N layers' expert weights stay on the CPU. |
| Tensor override       | `-ot <pattern>=<buffer type>`                  | `llama-bench` separates entries with `;`, the other two with `,`. |
| CPU threads           | `-t`                                           | `llama-bench` default is 10, the others -1. |
