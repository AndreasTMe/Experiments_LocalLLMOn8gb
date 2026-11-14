# Tools

.NET 10 file-based apps, standard library only. Run them from the repo root:

```
dotnet run tools/<name>.cs -- <arguments>
```

## gpu-sample.cs

Samples the GPU with Windows `nvidia-smi` and appends one CSV row per sample.

```
dotnet run tools/gpu-sample.cs -- --label <label> --out <file.csv> [--interval-ms 250] [--duration-s N]
```

Runs until Ctrl+C, or for `--duration-s` seconds. Every row is flushed as it's written, so the CSV is complete whenever the tool stops. Several runs can share one file; the header is written only when the file is new or empty.

| Column                | Unit     | `nvidia-smi` field            |
| --------------------- | -------- | ----------------------------- |
| `timestamp`           | ISO 8601 | `timestamp`                   |
| `label`               |          | from `--label`                |
| `memory_used_mib`     | MiB      | `memory.used`                 |
| `memory_total_mib`    | MiB      | `memory.total`                |
| `power_instant_w`     | W        | `power.draw.instant`          |
| `power_average_w`     | W        | `power.draw.average` (1 s)    |
| `temperature_c`       | °C       | `temperature.gpu`             |
| `sm_clock_mhz`        | MHz      | `clocks.sm`                   |
| `mem_clock_mhz`       | MHz      | `clocks.mem`                  |
| `pstate`              |          | `pstate`                      |
| `clock_event_reasons` | bitmask  | `clocks_event_reasons.active` |
| `gpu_utilization_pct` | %        | `utilization.gpu`             |

`clock_event_reasons` is `0x0000000000000001` when the GPU is idle and `0x0000000000000000` when nothing is holding the clocks back. Any other bit means throttling; see `nvidia-smi --help-query-gpu`.

## bench.cs

Runs a sweep of `llama-bench` configurations in the pinned llama.cpp image and appends one row per test to a results CSV, in the columns below.

```
dotnet run tools/bench.cs -- --sweep <sweep.csv> --experiment <name> [--out <results.csv>] [--cool-to-c 45] [--cool-timeout-s 900]
```

`--out` defaults to `experiments/<experiment>/results/bench.csv`. The raw files go into `raw/` next to it.

The sweep file has one configuration per line, one value per column:

```
name,model_file,n_gpu_layers,flash_attn,cache_type_k,cache_type_v,n_cpu_moe,threads,n_prompt,n_gen,depth_tokens,repetitions,notes
4b-q4km-f16-d0,Qwen3-4B-Q4_K_M.gguf,99,on,f16,f16,0,10,512,512,0,5,
```

`model_file` has to be in `models/models.csv`, and `name` ends up in file names, so it's letters, digits, `.`, `_` and `-` only. Set `n_prompt` or `n_gen` to `0` to skip that test.

For a sweep, the runner:

1. Starts `gpu-sample.cs` for the whole sweep, writing to `raw/<time>-<sweep>-gpu.csv`.
2. Before each configuration, waits until the GPU is at `--cool-to-c` (45 °C by default) or cooler. The target is fixed so it doesn't depend on how warm the GPU was when the sweep started. On the 3060 Ti, 45 °C takes about 3 minutes after a run; true idle (about 41 °C) takes about 10, because the fans stop at low temperatures. After `--cool-timeout-s` it runs anyway and says so in `notes`.
3. Runs `llama-bench` once per configuration, by digest from `docker/images.md`, with `--gpus all`, the models volume read-only, `-o json` and `--progress`. `llama-bench` does a warm-up run before each test, so the runner doesn't add one.
4. Saves the JSON as `raw/<run_id>.json` and the stderr as `raw/<run_id>.log`, with a host timestamp on every line.
5. Writes one row per test. The sampler columns use only the samples inside the test's timed runs: each run starts at its `prompt run k/R` or `generation run k/R` progress line and lasts as long as `samples_ns` says. Warm-up and depth runs are left out.

`bandwidth_ceiling_tokens_per_s` comes from `ceilings.csv` next to the results file, so run `ceiling.cs` first. If that file is missing, the runner says so and leaves the column empty. If `llama-bench` fails, the log is kept, no row is written, and the sweep moves on.

## ceiling.cs

Works out the memory-bandwidth ceiling on decode speed for every model in `models/models.csv`. It needs Docker and the pinned image, but no GPU and no downloads: it reads each model's GGUF header from the `llm-models` volume through the image, with `head`.

```
dotnet run tools/ceiling.cs -- --experiment <name> [--out <ceilings.csv>] [--vram-gbps 448] [--ram-gbps 64]
```

`--out` defaults to `experiments/<experiment>/results/ceilings.csv`. The file is rewritten on every run.

Decode reads every weight it uses once per token, so a token can't be faster than the time it takes to read those bytes:

```
seconds per token = GPU bytes / VRAM bandwidth + CPU bytes / RAM bandwidth
ceiling           = 1 / seconds per token
```

Each model gets one row per `-ngl` value, from `0` (CPU only) to `layers + 1` (everything on the GPU; `-ngl` counts the output layer as one). An MoE model also gets one row per `--n-cpu-moe` value from `1` to `layers`, with everything else on the GPU. Fitting in VRAM is a separate question: the full-GPU row is there even for models that don't fit.

**Bytes.** Each tensor's size comes from the model's own tensor table: the gap between its data offset and the next one's. That includes a few bytes of alignment padding per tensor.

**Placement.** It follows llama.cpp. `gpu_weights_bytes` matched the `CUDA0 model buffer size` that `llama-bench -v` logs to within 0.1 MiB, for full and partial offload, tied embeddings and `--n-cpu-moe`.

- `-ngl N` puts the output layer and the last `N − 1` repeating layers on the GPU.
- The token embedding table always stays in system RAM. Decode reads one row of it per token, so it counts as 0 bytes. With tied embeddings (the 4B) there's no output tensor, and the output reads the whole embedding table, so that table counts as the output layer.
- `--n-cpu-moe M` moves the expert tensors of the first `M` layers to system RAM.
- An MoE model reads `expert_used_count` of its `expert_count` experts per token (8 of 128 for Qwen3-30B-A3B), so expert tensors count at that share.

**Bandwidth.** 448 GB/s for the RTX 3060 Ti's GDDR6. 64 GB/s for the RAM, which is the theoretical figure for dual-channel DDR4 at 4000 MT/s (2 × 8 bytes × 4000 MT/s), not a measured one. GB here is 10⁹ bytes.

**Not counted.** Reading the KV cache. It's small at depth 0, but it grows with context, so the ceiling is too high at long depths.

| Column                 | Example                | Source                                                         |
| ---------------------- | ---------------------- | -------------------------------------------------------------- |
| `model_file`           | `Qwen3-8B-Q4_K_M.gguf` | `file_name`                                                    |
| `quant`                | `Q4_K_M`               | `quant`                                                        |
| `role`                 | `dense-8b`             | `role`                                                         |
| `layers`               | `36`                   | `layers`, checked against the GGUF's `block_count`             |
| `n_gpu_layers`         | `30`                   | `0` to `layers + 1`                                            |
| `n_cpu_moe`            | `0`                    | `0`, or `1` to `layers` for MoE models                         |
| `gpu_weights_bytes`    | `3841549312`           | bytes of the tensors on the GPU                                |
| `gpu_bytes_per_token`  | `3841549312`           | bytes one token reads from the GPU                             |
| `cpu_bytes_per_token`  | `830217216`            | bytes one token reads from system RAM                          |
| `vram_gbps`            | `448`                  | `--vram-gbps`                                                  |
| `ram_gbps`             | `64`                   | `--ram-gbps`                                                   |
| `ceiling_tokens_per_s` | `46.4`                 | `1 / (gpu_bytes_per_token / VRAM + cpu_bytes_per_token / RAM)` |

## Benchmark results

Each experiment keeps its benchmark results in `experiments/<experiment>/results/`:

- `bench.csv`: one row per configuration and test (pp or tg), in the columns below.
- `raw/<run_id>.json`: the unmodified `llama-bench -o json` output for each run. It's the evidence behind every row, and it's committed.

`fixtures/llama-bench-b12030.json` is a real `llama-bench -o json` output from the pinned image (build 12030), kept to test parsing against. It has four tests: pp512 and tg128, each at depth 0 and 4096, with a q8_0 KV cache:

```
llama-bench -m /models/Qwen3-4B-Q4_K_M.gguf -ngl 99 -fa on -ctk q8_0 -ctv q8_0 -p 512 -n 128 -d 0,4096 -r 3 -o json
```

### Columns

A row in `bench.csv` joins five sources, and the runner does the joining. Only the first group comes from the llama-bench JSON, so the fixture covers only those columns. The tables below are grouped by source; in the CSV the columns are in the order listed here.

Values are written as they come from the source. Times are ISO 8601 with offset, and units are in the column names.

**Runner.** Written by the runner for each test.

| Column       | Example                   | Source                               |
| ------------ | ------------------------- | ------------------------------------ |
| `run_id`     | `2026-11-03T101500-dense` | runner; also names the raw JSON file |
| `started_at` | `2026-11-02T20:03:21Z`    | when the test started, see below     |
| `ended_at`   | `2026-11-02T20:03:26Z`    | when the test finished, see below    |
| `experiment` | `01-how-much-fits`        | runner                               |
| `notes`      |                           | free text, no commas                 |

**`docker/images.md`.**

| Column         | Example            | Source                     |
| -------------- | ------------------ | -------------------------- |
| `image_digest` | `sha256:03d61e75…` | digest of the pinned image |

**llama-bench JSON.** One JSON object per test.

| Column                | Example                | Source                                          |
| --------------------- | ---------------------- | ----------------------------------------------- |
| `llama_build`         | `12030`                | `build_number`                                  |
| `llama_commit`        | `a91eab67d`            | `build_commit`                                  |
| `model_file`          | `Qwen3-4B-Q4_K_M.gguf` | file name from `model_filename`                 |
| `n_gpu_layers`        | `99`                   | `n_gpu_layers`                                  |
| `flash_attn`          | `1`                    | `flash_attn` (1 on, 0 off)                      |
| `cache_type_k`        | `q8_0`                 | `type_k`                                        |
| `cache_type_v`        | `q8_0`                 | `type_v`                                        |
| `n_cpu_moe`           | `0`                    | `n_cpu_moe`                                     |
| `threads`             | `10`                   | `n_threads`                                     |
| `test`                | `tg`                   | `pp` when `n_prompt` > 0, `tg` when `n_gen` > 0 |
| `n_tokens`            | `128`                  | `n_prompt` or `n_gen`                           |
| `depth_tokens`        | `4096`                 | `n_depth`                                       |
| `repetitions`         | `3`                    | length of `samples_ts`                          |
| `tokens_per_s_mean`   | `89.78`                | `avg_ts`                                        |
| `tokens_per_s_stddev` | `1.31`                 | `stddev_ts`                                     |

**`models/models.csv`.** Looked up by `model_file`.

| Column               | Example      | Source                         |
| -------------------- | ------------ | ------------------------------ |
| `model_sha256_short` | `7485fe6f`   | first 8 characters of `sha256` |
| `quant`              | `Q4_K_M`     | `quant`                        |
| `model_size_bytes`   | `2497280256` | `size_bytes` (the file size)   |

**`gpu-sample.cs` output.** Samples between `started_at` and `ended_at`.

| Column                     | Example              | Source                                        |
| -------------------------- | -------------------- | --------------------------------------------- |
| `vram_peak_mib`            | `4496`               | highest `memory_used_mib`                     |
| `power_mean_w`             | `61.2`               | mean `power_average_w`                        |
| `temperature_start_c`      | `41`                 | first `temperature_c`                         |
| `temperature_end_c`        | `63`                 | last `temperature_c`                          |
| `sm_clock_mean_mhz`        | `1770`               | mean `sm_clock_mhz`                           |
| `clock_event_reasons_seen` | `0x0000000000000000` | distinct `clock_event_reasons`, `;`-separated |

**`ceiling.cs` output.** Looked up by `model_file`, `n_gpu_layers` and `n_cpu_moe`. An `n_gpu_layers` above `layers + 1` (such as `99`) counts as all layers.

| Column                           | Example | Source                                                                                     |
| -------------------------------- | ------- | ------------------------------------------------------------------------------------------ |
| `bandwidth_ceiling_tokens_per_s` | `95.9`  | `ceiling_tokens_per_s`; tg rows only, and empty for `n_cpu_moe` with fewer than all layers |

Things the fixture shows that the columns have to deal with:

- **Model size.** JSON `model_size` (2491323904 for the 4B Q4_K_M) is the tensor data, not the file. `model_size_bytes` uses the file size from the manifest, which is what has to fit on the card.
- **The test window.** JSON `test_time` is the start of each test, to the second, and there's no end time. The window for the sampler columns has to come from the runner, which knows when each test started and finished. A tg test also has to run for several seconds, because `power.draw.average` is a 1-second average.
- **Flash attention** is `1` or `0` in the JSON, not `on` or `off`.

### What the columns are for

| Use                                | Columns                                                                                       |
| ---------------------------------- | --------------------------------------------------------------------------------------------- |
| Decode speed against GPU layers    | `tokens_per_s_mean` (tg) against `n_gpu_layers`, one `model_file`                             |
| Measured speed against the ceiling | `tokens_per_s_mean` (tg) against `bandwidth_ceiling_tokens_per_s`, per `model_file`           |
| Speed against context length       | `tokens_per_s_mean` (pp and tg) against `depth_tokens`, per `cache_type_k` and `cache_type_v` |
| Quality against VRAM               | `vram_peak_mib`, joined on `model_file` to the KL divergence and task results                 |
| Energy per token                   | `power_mean_w / tokens_per_s_mean` in joules per token (tg rows)                              |
| Was the row throttled?             | `clock_event_reasons_seen`, `temperature_end_c`, `sm_clock_mean_mhz`                          |
