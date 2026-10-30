# Estimates

Written before any benchmark ran. Not edited after the commit that added them.

Seen before writing these: the hardware inventory, including 1353 MiB of VRAM in use with a normal desktop open. No model has been run on this setup for this experiment.

## Estimates

| #   | Question                                                                      | Estimate                               |
| --- | ----------------------------------------------------------------------------- | -------------------------------------- |
| 1   | VRAM actually free with the desktop running                                   | 6.8 GB (6839 MiB)                      |
| 2   | Decode tokens/sec for an 8B Q4_K_M, as a percentage of the bandwidth limit    | 65% (about 59 tokens/sec)              |
| 3   | Decode speed with 80% of layers on the GPU, as a percentage of all on the GPU | 40%                                    |
| 4a  | Largest context for an 8B Q4_K_M with an f16 KV cache                         | 12k tokens                             |
| 4b  | Largest context for an 8B Q4_K_M with a q8_0 KV cache                         | 24k tokens                             |
| 5   | MoE with experts on the CPU, against the best dense model that fits           | Slower: about 25 against 59 tokens/sec |
| 6   | First quantisation where the task score drops noticeably                      | None, down to Q4_K_M                   |
| 7   | Months of API calls to pay for the GPU                                        | Never in practice (about 2,400 months) |

## Working

1. 8192 MiB total minus the 1353 MiB in use on a normal desktop. This is the busy desktop, not the minimal one.
2. Ceiling: 448 GB/s / 4.9 GB = 91 tokens/sec. The 65% is the middle of the plan's 60–70% guess, a judgement rather than a calculation. 91 x 0.65 = 59.
3. Bandwidth alone gives the upper bound: 1 / (0.8 + 0.2 x 7) = 45%, using the theoretical 7x ratio. Assuming the CPU reaches only half of the theoretical 64 GB/s makes the ratio about 9x: 1 / (0.8 + 0.2 x 9) = 38%. Rounded to 40%.
4. Assumes Qwen3 8B: 36 layers, 8 KV heads, head size 128.
    - KV cache at f16: 8 x 128 x 2 (K and V) x 2 bytes x 36 layers = 144 KiB per token.
    - Room for it: 6839 MiB free - 4673 MiB model (4.9 GB) - 400 MiB assumed for compute buffers = 1766 MiB.
    - 1766 MiB / 144 KiB = about 12k tokens. q8_0 uses half the bytes, so about 24k.
    - The compute buffer grows with context, so these lean high.
5. Assumes a ~1.9 GB active set at Q4 (a tenth of an ~18.6 GB file), split about 0.6 GB on the GPU and 1.3 GB of experts on the CPU. That split is a guess. At an effective 32 GB/s for the CPU: 1.3 / 32 + 0.6 / 448 = about 42 ms per token, or 24 tokens/sec. Dense figure from #2.
6. A judgement, with no data behind it. The tasks are short and checkable, and Q4_K_M is usually reported as close to the higher quants, and Q4_K_M is the coarsest quant in the sweep, so no noticeable drop is expected.
7. All placeholders until measured:
    - Energy: 200 W / 59 tokens/sec = 3.4 J per token, or 0.94 kWh per million tokens. At $0.25/kWh that's $0.24.
    - Hosted price for a comparable small model: $0.40 per million output tokens (from memory, not checked).
    - Saving: $0.16 per million tokens. At a GPU price of $399 and 1 million tokens a month, that's about 2,400 months, or 200 years.

## Definitions

- **Model for #3:** the 8B Q4_K_M, with all layers on the GPU as its own 100% baseline.
- **"Noticeably" for #6:** 3 or more fewer tasks out of 30 than Q8_0.
- **Assumptions for #7:** $399 GPU price (launch price from memory, not what I paid), 1 million tokens a month, $0.25/kWh, $0.40 per million tokens for the hosted model.

## Earlier rough guesses (from the plan)

- Windows takes 0.5–1 GB, so "8 GB" is closer to 7.
- Decode reaches 60–70% of the bandwidth limit.
- Speed falls off a cliff with the first offloaded layer, because system RAM is roughly ten times slower than VRAM.
- The MoE model with offloaded experts is surprisingly usable, because only ~3B parameters are active per token.
- The API payback is "never".
