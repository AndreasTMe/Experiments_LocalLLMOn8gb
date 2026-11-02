# VRAM and RAM baseline

Measured 2026-11-02 with `tools/gpu-sample.cs` and driver 617.42. Readings are in `results/baseline.csv`, raw GPU samples in `results/baseline-gpu-samples.csv`.

## 8 GB is really...

| Condition   | VRAM used     | VRAM left of 8192 MiB      |
| ----------- | ------------- | -------------------------- |
| Normal      | 1304–1321 MiB | 6871–6888 MiB (about 6.7 GiB) |
| Minimal     | 688 MiB       | 7504 MiB (about 7.3 GiB)   |
| Docker idle | 736–740 MiB   | 7452–7456 MiB (about 7.3 GiB) |

**8 GB is really 6.7 GiB with the desktop as I normally have it, and 7.3 GiB with everything closed and Docker Desktop running.** The normal desktop costs about 600 MiB of VRAM. Docker Desktop idle costs about 50 MiB.

## Conditions

- **Normal:** browser, IDEs, Docker Desktop and the usual tray apps open.
- **Minimal:** everything closed except the Claude desktop app and one PowerShell window, which were needed to take the readings. Docker Desktop quit. Some background apps still had a GPU context, and `nvidia-smi` can't show per-process VRAM under WDDM, so their share is unknown. This is "as much closed as possible", not the true floor.
- **Docker idle:** the minimal condition with Docker Desktop started and no containers running.

## Method

- Each reading is a 10-second sampler run at 250 ms (39 samples). The figure is the median of `memory.used`.
- Three readings per condition, taken one after another. All three agree within 17 MiB for VRAM and 70 MiB for Windows RAM.
- RAM available in Windows: the `\Memory\Available MBytes` performance counter.
- RAM available in a container: `MemAvailable` from `/proc/meminfo` in the pinned llama.cpp image.

## RAM

| Condition   | Available in Windows | Available in a container |
| ----------- | -------------------- | ------------------------ |
| Normal      | 40744–40762 MiB      | 31018–31035 MiB          |
| Minimal     | 50895–50907 MiB      | (Docker off)             |
| Docker idle | 49477–49546 MiB      | 31008–31058 MiB          |

The container sees about 31 GiB in every condition, because that's the WSL2 VM's limit (half the host RAM), not what Windows has free. The WSL VM (`vmmem`) still held about 1.5 GB in the minimal condition, after Docker Desktop had quit.

## CUDA's own figure

`llama-server --list-devices` in the pinned image reports `8191 MiB, 7126 MiB free` under Docker idle, three times in a row. It reported the same 7126 MiB with the normal desktop open, so under WSL2 it doesn't track what the desktop uses, and it isn't used as a measurement. What actually fits will come from the maximum-context runs.
