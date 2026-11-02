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

**Bandwidth ceiling calculator.**

| Column                           | Example | Source                                     |
| -------------------------------- | ------- | ------------------------------------------ |
| `bandwidth_ceiling_tokens_per_s` | `91.4`  | the calculator's estimate for `model_file` |

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
