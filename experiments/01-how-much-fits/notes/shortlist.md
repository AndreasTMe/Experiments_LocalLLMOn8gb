# Shortlist and task scores

Run on 2026-11-20 against the pinned image in `docker/images.md` (build 12030), with `tools/tasks.cs run` and the 30 tasks in `tasks/`. Summary rows in `results/tasks.csv`; replies, per-task results and server logs in `results/raw/`, named after the run id.

## Shortlist

Every configuration runs with ctx 4096, flash attention on, 10 threads, and the generation settings in `tasks/README.md`.

| Configuration                 | GPU layers                       | KV cache   | Why it's on the list                                      |
| ----------------------------- | -------------------------------- | ---------- | --------------------------------------------------------- |
| 4B Q8_0, Q6_K, Q5_K_M, Q4_K_M | all                              | f16        | the quant ladder against Q8_0, next to the KL divergence  |
| 8B Q8_0                       | 30 (the fit)                     | f16        | the base of the 8B ladder; it doesn't fit whole           |
| 8B Q6_K, Q5_K_M, Q4_K_M       | all                              | f16        | the rest of the 8B ladder, all fully on the GPU           |
| 8B Q4_K_M                     | all                              | q8_0, q4_0 | what KV cache quantisation does at short context          |
| 14B Q4_K_M                    | 32 (the fit)                     | f16        | the biggest dense model, at the edge of the offload cliff |
| 30B-A3B Q4_K_M                | all, 33 expert layers on the CPU | f16        | the MoE model at its fastest setting                      |

Long-context quality isn't covered here; the tasks are short, a few hundred tokens each. It has its own check in `long-context.md`.

## Scores

Right out of 30.

| Configuration          | KV   | All | Code (10) | Extraction (8) | Arithmetic (6) | Logic (6) | Cut off | Generation s |
| ---------------------- | ---- | --: | --------: | -------------: | -------------: | --------: | ------: | -----------: |
| 4B Q8_0                | f16  |  17 |         5 |              3 |              5 |         4 |       1 |           96 |
| 4B Q6_K                | f16  |  16 |         5 |              3 |              4 |         4 |       0 |           78 |
| 4B Q5_K_M              | f16  |  18 |         6 |              3 |              4 |         5 |       1 |           72 |
| 4B Q4_K_M              | f16  |  16 |         6 |              3 |              4 |         3 |       1 |           63 |
| 8B Q8_0 (30 layers)    | f16  |  22 |         7 |              5 |              4 |         6 |       0 |          404 |
| 8B Q6_K                | f16  |  18 |         6 |              5 |              3 |         4 |       1 |          150 |
| 8B Q5_K_M              | f16  |  20 |         7 |              4 |              4 |         5 |       1 |          134 |
| 8B Q4_K_M              | f16  |  20 |         8 |              5 |              4 |         3 |       1 |          120 |
| 8B Q4_K_M              | q8_0 |  23 |         8 |              7 |              4 |         4 |       0 |          118 |
| 8B Q4_K_M              | q4_0 |  22 |         7 |              5 |              5 |         5 |       1 |          124 |
| 14B Q4_K_M (32 layers) | f16  |  24 |         7 |              6 |              5 |         6 |       0 |          467 |
| 30B-A3B Q4_K_M         | f16  |  21 |         6 |              6 |              6 |         3 |       0 |          277 |

## Repeats

- 14B Q4_K_M was run twice: 24 both times, with the same reply to every task, byte for byte.
- 4B Q4_K_M was also run twice the day before (`results/tasks-repeat.csv`). The run here gave the same replies again.

So a configuration's score doesn't move between runs. A difference between two configurations comes from the configuration.

## What the scores say

- Size matters more than quantisation. 4B scores 16–18 at every quant, 8B 18–23, 14B 24.
- Within one model, the scores don't fall steadily as the quant gets coarser. 4B Q5_K_M beats Q8_0 by 1, and 8B Q6_K is the lowest of the 8B ladder (18, against 20 for Q5_K_M and Q4_K_M). Changing the quant, or the KV cache type, changes which tasks happen to land on the right side, and that moves the score by a few tasks either way. With 30 tasks, a difference of 3 or 4 is within that.
- By the rule in `estimates.md` (3 or more fewer than Q8_0), 4B never drops, and 8B "drops" at Q6_K (−4) but not at Q5_K_M or Q4_K_M (−2). That isn't a quantisation trend: the coarser quants score higher. The KL divergence in `results/kld.csv` is the cleaner measure of what each step loses; the task score says the loss doesn't show on these tasks.
- KV cache quantisation costs nothing visible at this length: 8B Q4_K_M scores 20 with f16, 23 with q8_0 and 22 with q4_0.
- The MoE model (21) lands between 8B and 14B, and its replies took about 60% of the 14B's time (277 s against 467 s).
- Many code failures are build errors, not wrong logic: a missing `using System.Text;`, `Math.Min` with three arguments, a `long` returned as `int`. These are scored as wrong, like any other reply that doesn't build.
