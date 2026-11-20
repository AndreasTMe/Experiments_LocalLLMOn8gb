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

## kld.cs

Measures how far each quantisation drifts from Q8_0, with `llama-perplexity`'s KL divergence, and appends one row per quant to a results CSV.

```
dotnet run tools/kld.cs -- --experiment <name> [--out <kld.csv>] [--ctx 512] [--chunks 100]
dotnet run tools/kld.cs -- --experiment <name> --parse <run_id> [--out <kld.csv>]
```

`--out` defaults to `experiments/<experiment>/results/kld.csv`. The logs go into `raw/` next to it, as `raw/<run_id>-kld-<model>-<quant>.log`. `--parse` runs nothing: it rebuilds the rows of an earlier run from those logs.

It needs two Docker volumes: `llm-models` with the models, and `llm-scratch` with the wikitext-2 test text at `/scratch/text/wiki.test.raw`. The runner checks the text's SHA-256 before it starts.

For each of the Qwen3 4B and 8B, the runner:

1. Runs Q8_0 with `--kl-divergence-base` to save its logits to `/scratch/logits/` in `llm-scratch`. That's about 7.7 GB per model at 512 tokens × 100 chunks.
2. Runs Q8_0 again with `--kl-divergence` against those logits. It's a self-check: the KL divergence should be about 0 and the top token the same about 100 % of the time.
3. Runs Q6_K, Q5_K_M and Q4_K_M with `--kl-divergence` against the same logits.

Every run uses `-fa on -t 10` and `-ngl 99`, except the 8B Q8_0, which doesn't fit in 8 GB and runs with `-ngl 30`. The whole set takes about 5 minutes on the 3060 Ti.

`ppl_ratio` is computed against the Q8_0 run's own final perplexity, not the `Mean PPL(base)` that the KL runs print. That one is computed from the saved logits, which are stored at reduced precision, and reads about 1 % low.

| Column               | Example                | Source                                                  |
| -------------------- | ---------------------- | ------------------------------------------------------- |
| `run_id`             | `2026-11-19T163033`    | runner; also starts the log file names                  |
| `model_file`         | `Qwen3-4B-Q4_K_M.gguf` | the quant being measured                                |
| `quant`              | `Q4_K_M`               | the quant being measured                                |
| `base_file`          | `Qwen3-4B-Q8_0.gguf`   | the model the base logits come from                     |
| `text`               | `wikitext-2 test`      | runner                                                  |
| `text_sha256_short`  | `173c87a5`             | first 8 characters of the text's SHA-256                |
| `ctx`                | `512`                  | `n_ctx` in the log                                      |
| `chunks`             | `100`                  | chunks in the log                                       |
| `ppl`                | `16.4561`              | `Mean PPL(Q)`                                           |
| `ppl_err`            | `0.3729`               | its `±`                                                 |
| `ppl_base`           | `14.4136`              | `Final estimate` of the Q8_0 base run                   |
| `ppl_ratio`          | `1.1417`               | `ppl / ppl_base`                                        |
| `kld_mean`           | `0.134937`             | `Mean KLD`                                              |
| `kld_mean_err`       | `0.002469`             | its `±`                                                 |
| `kld_median`         | `0.042891`             | `Median KLD`                                            |
| `kld_p99`            | `1.509506`             | `99.0% KLD`                                             |
| `kld_max`            | `16.552729`            | `Maximum KLD`                                           |
| `same_top_pct`       | `86.118`               | `Same top p`, in %                                      |
| `same_top_err`       | `0.217`                | its `±`                                                 |
| `rms_dp_pct`         | `10.687`               | `RMS Δp`, in %                                          |
| `n_gpu_layers`       | `99`                   | `-ngl`                                                  |
| `image_digest_short` | `03d61e75`             | first 8 characters of the digest in `docker/images.md`  |

With `--parse`, `image_digest_short` is the image pinned now, not necessarily the one the run used.

## tasks.cs

Checks the task set in `tasks/`, scores replies against it, and runs it against a model in `llama-server`. The format, the checkers, the prompt and the generation settings are described in `tasks/README.md`.

```
dotnet run tools/tasks.cs -- validate [--tasks tasks/tasks.jsonl]
dotnet run tools/tasks.cs -- check --answers <answers.jsonl> [--tasks tasks/tasks.jsonl]
dotnet run tools/tasks.cs -- run --experiment <name> --model <file.gguf> [--out <tasks.csv>]
    [--gpu-layers 99] [--ctx 4096] [--flash-attn on] [--cache-type-k f16] [--cache-type-v f16]
    [--n-cpu-moe 0] [--threads 10] [--port 8090] [--tasks tasks/tasks.jsonl]
```

`validate` checks every line of the tasks file: the fields and their types, unique ids, the category and checker names, the type of `expected` for the checker, and that the tests and reference files exist. It prints each problem with its line number and exits with 1 if there's any.

`check` validates the tasks first, then scores an answers file. That's JSON Lines with one object per task:

```
{"id": "math-train", "reply": "The train covers ...
Answer: 205"}
```

`reply` is the model's reply as it came back. Other fields are ignored, and a task with no line counts as wrong. An unknown id or a second reply for the same id stops the check.

It prints one line per task, `right` or `wrong` with a short reason, then the score per category and overall:

```
code-roman          csharp-tests  wrong  tests failed: ToRoman(4): expected "IV", got "IIII"
ext-logs            json          wrong  services_with_errors: missing
math-tank           number        wrong  not a plain number: 3 units

code          9 of  10   90.0 %
```

Code replies run in the .NET SDK image, by digest from `docker/images.md`, with the limits in `tasks/README.md`. The tool never pulls; it stops if the image isn't on this machine. One container per code task, about 2.5 seconds each.

### run

`run` validates the tasks, then starts `llama-server` in the pinned llama.cpp image, by digest, with the model from the `llm-models` volume and the configuration given. The model has to be in `models/models.csv`. The server listens on `127.0.0.1:<port>` only, and its log goes to a file. The tool waits up to 120 seconds for `/health`, then sends the tasks one at a time to `/v1/chat/completions`, with 300 seconds per request. If the server stops, isn't ready in time, or a request fails, the run stops and exits with 1. The server is stopped before the replies are scored, so the code checks don't share the machine with it.

With `--experiment <name>`, the summary row goes to `experiments/<name>/results/tasks.csv` unless `--out` says otherwise. The other files go to `raw/` next to it, named after the run id, `<yyyy-MM-ddTHHmmss>-<model file without .gguf>`:

| File                     | What's in it                                                                                     |
| ------------------------ | ------------------------------------------------------------------------------------------------ |
| `<run_id>-answers.jsonl` | one line per task: `id`, `reply`, `finish_reason`, `prompt_tokens`, `completion_tokens`, `seconds`; `check` reads it as it is |
| `<run_id>-tasks.csv`     | one row per task: `id`, `category`, `checker`, `right`, `finish_reason`, `prompt_tokens`, `completion_tokens`, `seconds`, `tokens_per_s`, `detail` |
| `<run_id>-server.log`    | the server's output                                                                              |

`detail` is the reason a task is wrong, quoted when it has a comma in it. `tokens_per_s` is the server's own generation speed for that reply.

The summary row has the configuration (`model_file`, `quant`, `n_gpu_layers`, `ctx`, `flash_attn`, `cache_type_k`, `cache_type_v`, `n_cpu_moe`, `threads`), the score (`tasks`, `right`, `score_pct` and `code_pct`, `extraction_pct`, `arithmetic_pct`, `logic_pct`), `cut_off` (replies that hit the token limit), `completion_tokens` and `generation_s` summed over the tasks, `started_at`, `ended_at`, and the first 8 hex characters of the tasks file's SHA-256 and of the image digest. The header is written when the file is new.

On Qwen3-4B Q4_K_M with every layer on the GPU, a run takes about 70 seconds for the replies plus about 25 seconds for the code checks. Two runs gave the same reply for every task, byte for byte.

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

### llama-bench JSON properties

`raw/<run_id>.json` is an array with one object per test: a pp test when `n_prompt` > 0, a tg test when `n_gen` > 0. Every object has the properties below, in this order. The examples are from the fixture's first test. Most properties echo a `llama-bench` flag, named as in `--help` of the pinned image; a flag the runner doesn't pass keeps its default.

**Build and machine.**

| Property       | Example                                 | Meaning                             | Results column |
| -------------- | --------------------------------------- | ----------------------------------- | -------------- |
| `build_commit` | `a91eab67d`                             | llama.cpp commit of the image       | `llama_commit` |
| `build_number` | `12030`                                 | llama.cpp build number of the image | `llama_build`  |
| `cpu_info`     | `12th Gen Intel(R) Core(TM) i7-12700KF` | CPU name                            |                |
| `gpu_info`     | `NVIDIA GeForce RTX 3060 Ti`            | GPU name                            |                |
| `backends`     | `CUDA`                                  | compute backends in the build       |                |

**Model.**

| Property         | Example                        | Meaning                                                      | Results column                |
| ---------------- | ------------------------------ | ------------------------------------------------------------ | ----------------------------- |
| `model_filename` | `/models/Qwen3-4B-Q4_K_M.gguf` | `-m`, the path inside the container                          | `model_file` (file name only) |
| `model_type`     | `qwen3 4B Q4_K - Medium`       | architecture, size and quantisation, as llama.cpp names them |                               |
| `model_size`     | `2491323904`                   | bytes of tensor data, not the file size                      |                               |
| `model_n_params` | `4022468096`                   | number of parameters                                         |                               |

**Settings.**

| Property                | Example | Flag           | Meaning                                                   | Results column |
| ----------------------- | ------- | -------------- | --------------------------------------------------------- | -------------- |
| `n_batch`               | `2048`  | `-b`           | logical batch size                                        |                |
| `n_ubatch`              | `512`   | `-ub`          | physical batch size                                       |                |
| `n_threads`             | `10`    | `-t`           | CPU threads                                               | `threads`      |
| `cpu_mask`              | `0x0`   | `-C`           | CPU affinity mask; `0x0` is none                          |                |
| `cpu_strict`            | `false` | `--cpu-strict` | strict CPU placement                                      |                |
| `poll`                  | `50`    | `--poll`       | polling level while waiting for work, 0–100               |                |
| `type_k`                | `q8_0`  | `-ctk`         | KV cache type for K                                       | `cache_type_k` |
| `type_v`                | `q8_0`  | `-ctv`         | KV cache type for V                                       | `cache_type_v` |
| `n_gpu_layers`          | `99`    | `-ngl`         | layers on the GPU, as passed                              | `n_gpu_layers` |
| `n_cpu_moe`             | `0`     | `-ncmoe`       | layers whose MoE experts stay in system RAM               | `n_cpu_moe`    |
| `split_mode`            | `layer` | `-sm`          | how a model is split across several GPUs                  |                |
| `main_gpu`              | `0`     | `-mg`          | main GPU index                                            |                |
| `no_kv_offload`         | `false` | `-nkvo`        | `true` keeps the KV cache in system RAM                   |                |
| `flash_attn`            | `1`     | `-fa`          | flash attention, `1` on and `0` off                       | `flash_attn`   |
| `devices`               | `auto`  | `-dev`         | devices to use                                            |                |
| `tensor_split`          | `0.00`  | `-ts`          | share of the model per GPU                                |                |
| `tensor_buft_overrides` | `none`  | `-ot`          | tensor-to-buffer overrides                                |                |
| `load_mode`             | `auto`  | `-lm`          | how weights are loaded: mmap, mlock or direct I/O         |                |
| `lazy_mode`             | `auto`  | `-lzm`         | the `-lzm` setting                                        |                |
| `embeddings`            | `false` | `-embd`        | embeddings mode instead of generation                     |                |
| `no_op_offload`         | `0`     | `-nopo`        | the `-nopo` setting                                       |                |
| `no_host`               | `false` | `--no-host`    | the `--no-host` setting                                   |                |
| `repack`                | `true`  | `--repack`     | weight repacking for the CPU backend                      |                |
| `fit_target`            | `0`     | `-fitt`        | margin in MiB for fitting the model to memory; `0` is off |                |
| `fit_min_ctx`           | `0`     | `-fitc`        | minimum context for `-fitt`                               |                |

**The test and its result.**

| Property     | Example                             | Meaning                                                          | Results column             |
| ------------ | ----------------------------------- | ---------------------------------------------------------------- | -------------------------- |
| `n_prompt`   | `512`                               | `-p`, prompt tokens in a pp test; `0` in a tg test               | `n_tokens`, `test`         |
| `n_gen`      | `0`                                 | `-n`, generated tokens in a tg test; `0` in a pp test            | `n_tokens`, `test`         |
| `n_depth`    | `0`                                 | `-d`, tokens already in the cache before the test                | `depth_tokens`             |
| `test_time`  | `2026-11-02T20:03:15Z`              | when the test started, to the second                             |                            |
| `avg_ns`     | `115602100`                         | mean time of one repetition, in nanoseconds                      |                            |
| `stddev_ns`  | `2990650`                           | standard deviation of that time                                  |                            |
| `avg_ts`     | `4430.982942`                       | mean tokens per second                                           | `tokens_per_s_mean`        |
| `stddev_ts`  | `115.834293`                        | standard deviation of tokens per second                          | `tokens_per_s_stddev`      |
| `samples_ns` | `[116460200, 112276200, 118069900]` | time of each repetition; the runner uses it for the test windows |                            |
| `samples_ts` | `[4396.35, 4560.18, 4336.41]`       | tokens per second of each repetition                             | `repetitions` (its length) |

The times cover only the `n_prompt` or `n_gen` tokens. Filling the cache to `n_depth` and the warm-up run aren't timed.

### What the columns are for

| Use                                | Columns                                                                                       |
| ---------------------------------- | --------------------------------------------------------------------------------------------- |
| Decode speed against GPU layers    | `tokens_per_s_mean` (tg) against `n_gpu_layers`, one `model_file`                             |
| Measured speed against the ceiling | `tokens_per_s_mean` (tg) against `bandwidth_ceiling_tokens_per_s`, per `model_file`           |
| Speed against context length       | `tokens_per_s_mean` (pp and tg) against `depth_tokens`, per `cache_type_k` and `cache_type_v` |
| Quality against VRAM               | `vram_peak_mib`, joined on `model_file` to the KL divergence and task results                 |
| Energy per token                   | `power_mean_w / tokens_per_s_mean` in joules per token (tg rows)                              |
| Was the row throttled?             | `clock_event_reasons_seen`, `temperature_end_c`, `sm_clock_mean_mhz`                          |
