# Energy and API payback

Worked out on 2026-11-21 from results already collected. Nothing new was run.

## Where the numbers come from

Speed and GPU power are from the `llama-bench` rows below, pp512 and tg512 at depth 0. The power is `power_mean_w`, the mean GPU board power over the test, sampled every 250 ms with `nvidia-smi`.

| Configuration          | File                         | Run id                                   | tg512 t/s | tg power W | pp512 t/s |
| ---------------------- | ---------------------------- | ---------------------------------------- | --------: | ---------: | --------: |
| 4B Q4_K_M              | `results/dense-full-gpu.csv` | `2026-11-13T231159-4b-q4km-faon`         |    114.18 |      217.9 |      4503 |
| 8B Q4_K_M              | `results/dense-full-gpu.csv` | `2026-11-13T231159-8b-q4km-faon`         |     72.14 |      218.9 |      2640 |
| 14B Q4_K_M (32 layers) | `results/offload-cliff.csv`  | `2026-11-14T102124-14b-q4km-ngl32`       |     17.38 |       94.3 |       854 |
| 30B-A3B Q4_K_M         | `results/moe.csv`            | `2026-11-18T204604-30b-a3b-q4km-ncmoe33` |     28.34 |       59.7 |       427 |

All four run with flash attention on, f16 KV cache and 10 threads. 30B-A3B has every layer on the GPU and the experts of 33 layers on the CPU.

Prices, all read on 2026-11-21:

| Item                  | Price                                    | Source                                                         |
| --------------------- | ---------------------------------------- | -------------------------------------------------------------- |
| Claude Haiku 5.5      | $0.10 input, $0.50 output per 1M tokens  | https://platform.claude.com/docs/en/about-claude/pricing       |
| Qwen3-30B-A3B, hosted | $0.12 input, $0.50 output per 1M tokens  | https://openrouter.ai/qwen/qwen3-30b-a3b (DeepInfra, standard) |
| Dollar to pound       | 1 USD = 0.7640 GBP, mid-market           | https://wise.com/gb/currency-converter/usd-to-gbp-rate         |
| Electricity           | 31.65p per kWh day, 13.72p per kWh night | my own tariff                                                  |
| GPU                   | £480                                    | what the RTX 3060 Ti cost me in 2022                           |

Haiku 5.5's price is for prompts up to 100,000 tokens. Above that it's $0.50 input and $2.50 output. OpenRouter also lists cheaper and dearer tiers for Qwen3-30B-A3B ($0.096 / $0.40 and $0.18 / $0.75); the standard one is used here.

## Method

- Output energy per token is tg power divided by tg speed.
- Input energy per token is 220 W divided by pp speed. The pp512 tests take 0.1–0.2 s each, which is too short for a 250 ms sampler, so their power readings (86–160 W) aren't reliable. 220 W is the most the card drew in any run here, so this is an upper bound.
- The token mix is 3 input tokens for every output token, so a mixed token is 0.75 input plus 0.25 output.
- Hours per 1M mixed tokens is 750,000 ÷ pp speed plus 250,000 ÷ tg speed.
- The API cost per 1M mixed tokens is 0.75 × input price plus 0.25 × output price: 15.28p for Haiku 5.5 and 16.43p for Qwen3-30B-A3B.
- Payback is the £480 spread over 36 months, £13.33 a month, divided by what each 1M tokens saves (API cost minus electricity).

## Energy per token

GPU board only. The CPU and the rest of the PC aren't in these numbers.

| Configuration | Output J/token | Input J/token | Mixed J/token | kWh per 1M | Cost per 1M, day | Cost per 1M, night | Hours per 1M |
| ------------- | -------------: | ------------: | ------------: | ---------: | ---------------: | -----------------: | -----------: |
| 4B Q4_K_M     |           1.91 |         0.049 |         0.514 |      0.143 |            4.52p |              1.96p |         0.65 |
| 8B Q4_K_M     |           3.03 |         0.083 |         0.821 |      0.228 |            7.22p |              3.13p |         1.04 |
| 14B Q4_K_M    |           5.43 |         0.258 |         1.550 |      0.431 |           13.63p |              5.91p |         4.24 |
| 30B-A3B       |           2.11 |         0.515 |         0.913 |      0.254 |            8.03p |              3.48p |         2.94 |

## Monthly cost

In pounds, for 1M, 10M and 100M mixed tokens a month. Electricity is shown as day / night.

| Option                |          1M |         10M |         100M | Hours at 100M |
| --------------------- | ----------: | ----------: | -----------: | ------------: |
| Claude Haiku 5.5      |        0.15 |        1.53 |        15.28 |           — |
| Qwen3-30B-A3B, hosted |        0.16 |        1.64 |        16.43 |           — |
| 4B Q4_K_M, local      | 0.05 / 0.02 | 0.45 / 0.20 |  4.52 / 1.96 |            65 |
| 8B Q4_K_M, local      | 0.07 / 0.03 | 0.72 / 0.31 |  7.22 / 3.13 |           104 |
| 14B Q4_K_M, local     | 0.14 / 0.06 | 1.36 / 0.59 | 13.63 / 5.91 |           424 |
| 30B-A3B, local        | 0.08 / 0.03 | 0.80 / 0.35 |  8.03 / 3.48 |           294 |

## Payback

The monthly volume at which £480 pays back in 3 years, with the GPU hours it takes each month. A month has about 730 hours.

| Configuration | Against Haiku 5.5, day | Against Haiku 5.5, night | Against hosted Qwen, day | Against hosted Qwen, night |
| ------------- | ---------------------- | ------------------------ | ------------------------ | -------------------------- |
| 4B Q4_K_M     | 124M (81 h)            | 100M (65 h)              | 112M (73 h)              | 92M (60 h)                 |
| 8B Q4_K_M     | 165M (172 h)           | 110M (114 h)             | 145M (151 h)             | 100M (104 h)               |
| 14B Q4_K_M    | 806M (3417 h)          | 142M (603 h)             | 476M (2019 h)            | 127M (537 h)               |
| 30B-A3B       | 184M (540 h)           | 113M (332 h)             | 159M (466 h)             | 103M (303 h)               |

The like-for-like row is 30B-A3B against hosted Qwen3-30B-A3B: the same model, run here or by someone else. The 4B, 8B and 14B aren't as good as Haiku 5.5, so those columns compare cost, not equal output.

## What the numbers say

- On electricity alone, local is cheaper per token than either API: 4.5–13.6p per 1M at the day rate and 2–6p at night, against 15–16p.
- The saving is small next to the card. At 1M or 10M tokens a month, the GPU doesn't pay back in 3 years against either API. It takes about 100M tokens a month or more, which keeps the GPU busy 60–540 hours a month.
- 14B at the day rate can't pay back at all: it would need more hours than a month has.
- If the GPU is already in the PC for other reasons, the question is only the electricity, and local wins on that.

## What isn't counted

- The CPU, memory, disks and power supply losses. This matters most for 14B and 30B-A3B, where the CPU does a large share of the work, so their energy numbers are lowest-case.
- Idle power while the model is loaded and waiting.
- The rest of the PC's cost, and the GPU's resale value.
- Input power is an upper bound, from the 220 W cap, not a measurement.
- 30B-A3B's generation speed drifts down over repeated runs (see `ollama.md`), so its figures move by a few percent between runs.
- Haiku 5.5's higher price above 100,000-token prompts. No prompt here is that long.
- Prices change. These were read on one day.

## Recommendations for 8 GB

From the task scores in `shortlist.md` and the long-context check in `long-context.md`.

| Use            | Configuration                      | Why                                                                                         |
| -------------- | ---------------------------------- | ------------------------------------------------------------------------------------------- |
| Chat           | 8B Q4_K_M, all on the GPU, q8_0 KV | 23 of 30, about 72 t/s; the best score that fits whole on the card                          |
| Code           | 14B Q4_K_M at `-ngl 32`            | 24 of 30, the highest score, but about 17 t/s and four times slower than 8B on the tasks    |
| Code, faster   | 8B Q4_K_M, all on the GPU, q8_0 KV | 23 of 30 in a quarter of 14B's time                                                         |
| Long documents | 8B Q4_K_M, q8_0 KV up to 32k       | 5 of 5 at 16k and 32k; about 33 t/s at 32k                                                  |
| Longest        | 8B Q4_K_M, q4_0 KV up to 40k       | 5 of 5 at 39k, the full 40960 trained context at full speed; about 29 t/s                   |
| Alternative    | 30B-A3B Q4_K_M, `--n-cpu-moe 33`   | 21 of 30 at about 28 t/s; the only one with a like-for-like hosted price to compare against |

4B is the fastest and cheapest per token, but it scored 16–18 at every quant, a clear step below 8B.
