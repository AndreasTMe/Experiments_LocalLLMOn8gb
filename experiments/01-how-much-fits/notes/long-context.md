# Long-context retrieval

Run on 2026-11-20 against the pinned image in `docker/images.md` (build 12030), with `tools/tasks.cs run`. Summary rows in `results/long-context.csv`; replies, per-task results and server logs in `results/raw/`, named after the run id.

## What was asked

Five made-up facts were planted in the start of WikiText-2's test text (`wiki.test.raw`), cut to fit the context. Four sit early, at 3 %, 6 %, 9 % and 9.5 % of the text, and a control sits in the middle. After the text comes one question about one fact, so each context size is five requests. The facts, questions and answers are in `tasks/long-context/`, and the files were built with `tools/long-context.cs`.

None of the names or numbers in the facts appears in the text, so a right answer has to come from the planted sentence.

## Configurations

All on Qwen3-8B Q4_K_M with every layer on the GPU, flash attention on, 10 threads, and the generation settings in `tasks/README.md`. Each context stays at or under the largest that ran at full speed for its KV cache type in `results/max-context.csv`: about 21k for f16, 34k for q8_0, and the full 40960 for q4_0. 39k is 39936, just under the 40960 the model was trained for.

| Context | KV cache        | Prompt tokens |
| ------: | --------------- | ------------: |
|     16k | f16, q8_0, q4_0 | 14,921–14,940 |
|     32k | q8_0, q4_0      | 31,313–31,332 |
|     39k | q4_0            | 38,632–38,651 |

## Results

Right out of 5.

| Context | KV   | Right | Seconds per request | Generation t/s |
| ------: | ---- | ----: | ------------------: | -------------: |
|     16k | f16  |     5 |             7.5–8.9 |          50–52 |
|     16k | q8_0 |     5 |             7.8–9.2 |          45–46 |
|     16k | q4_0 |     5 |             7.7–9.1 |          45–46 |
|     32k | q8_0 |     5 |           19.9–21.7 |             33 |
|     32k | q4_0 |     5 |           19.8–21.4 |             32 |
|     39k | q4_0 |     5 |           26.6–28.6 |             29 |

Every reply quoted the planted sentence and gave the right answer. None was cut off.

## What the results say

- Quantising the KV cache to q8_0 or q4_0 didn't stop the model finding a fact near the start of a long text, up to 39k tokens. Neither did context length.
- This is the easy case. Each fact is one sentence with names found nowhere else, and it's written in plain English among wikitext's spaced-out punctuation, so it stands out. The questions repeat the fact's own words. Questions that need several facts combined, or a fact that blends in, could still show a difference. This check doesn't cover that.
- The time is mostly reading the prompt: about 8 s at 16k, 20 s at 32k and 27 s at 39k per request, with each reply under 90 tokens. The prompt is read again for every question, because the prompt cache is off.
- Generation slows as the context fills: about 50 t/s at 16k with f16, 33 at 32k and 29 at 39k. That matches the speeds in `results/max-context.csv`, so nothing spilled out of GPU memory.
