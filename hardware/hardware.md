# Hardware

Recorded 2026-10-29 with read-only commands. Re-check the driver, Docker Desktop and WSL versions before each experiment, since they change with updates.

## GPU

| Field                  | Value                      | Source |
|------------------------|----------------------------|--------|
| Model                  | NVIDIA GeForce RTX 3060 Ti | `nvidia-smi --query-gpu=name` |
| VRAM                   | 8192 MiB GDDR6             | `nvidia-smi --query-gpu=memory.total` |
| Memory bandwidth       | 448 GB/s (spec sheet)      | NVIDIA specification, not measured |
| Compute capability     | 8.6 (Ampere)               | `nvidia-smi --query-gpu=compute_cap` |
| Driver                 | 560.94, WDDM               | `nvidia-smi` |
| Highest CUDA supported | 12.6                       | `nvidia-smi` header |
| VBIOS                  | 94.04.6b.00.a2             | `nvidia-smi --query-gpu=vbios_version` |
| Power limit            | 220 W                      | `nvidia-smi --query-gpu=power.limit` |
| PCIe link, max         | Gen 4 x16                  | `nvidia-smi --query-gpu=pcie.link.gen.max,pcie.link.width.max` |
| PCIe link, current     | Gen 4 x16 (at idle)        | `nvidia-smi --query-gpu=pcie.link.gen.current,pcie.link.width.current` |

At the time of recording, with a normal desktop open (browser, IDEs, Docker Desktop, about 20 graphical processes), 1353 MiB of VRAM was in use. This is not the baseline, which needs its own measurement.

## CPU

| Field           | Value                                                       | Source |
|-----------------|-------------------------------------------------------------|--------|
| Model           | 12th Gen Intel Core i7-12700KF                              | `Get-CimInstance Win32_Processor` |
| Cores / threads | 12 cores (8 performance + 4 efficiency) / 20 threads        | `Win32_Processor` core and thread counts; P/E split from Intel's specification |
| Integrated GPU  | None (KF model). The only display adapter is the 3060 Ti.   | `Get-CimInstance Win32_VideoController` |

## RAM

| Field                  | Value                                                             | Source |
|------------------------|-------------------------------------------------------------------|--------|
| Total                  | 64 GB (4 x 16 GB DDR5)                                            | `Get-CimInstance Win32_PhysicalMemory` |
| Modules                | Corsair CMK32GX5M2B5200C40, two 2 x 16 GB kits                    | same |
| Rated speed            | 5200 MT/s (kit rating)                                            | part number |
| Configured speed       | 4000 MT/s                                                         | `ConfiguredClockSpeed` |
| Channels               | 2 (Controller0 and Controller1, two DIMMs each)                   | `DeviceLocator` |
| Theoretical bandwidth  | 4000 MT/s x 8 bytes x 2 channels = **64 GB/s**                    | calculated |

VRAM bandwidth is 448 / 64 = **7x** the RAM bandwidth. That ratio is what sets the size of the offload cliff.

## Software

| Field          | Value                                   | Source |
|----------------|-----------------------------------------|--------|
| Windows        | Windows 10 Pro, build 19045.7725        | `wsl --version` |
| Docker Desktop | 4.90.0 (238679), engine 29.7.2          | `docker version` |
| WSL            | 3.0.1.0, kernel 6.18.40.1               | `wsl --version` |
| GPU runtime    | `nvidia` runtime registered in Docker   | `docker info` |

## WSL2 and Docker limits

| Field                           | Value                                                    | Source |
|---------------------------------|----------------------------------------------------------|--------|
| `.wslconfig`                    | Absent, so WSL defaults apply                            | `%USERPROFILE%\.wslconfig` does not exist |
| Memory available to Docker      | 31.25 GiB (WSL default: half the host RAM)               | `docker info` `MemTotal` = 33557102592 bytes |
| CPUs available to Docker        | 20                                                       | `docker info` `NCPU` |

## Disk

| Field                    | Value                                                                         | Source |
|--------------------------|-------------------------------------------------------------------------------|--------|
| Docker data disk         | `L:\Docker\DockerDesktopWSL\disk\docker_data.vhdx`, currently 61 GB (moved from C: on 2026-10-29) | `CustomWslDistroDir` in `%APPDATA%\Docker\settings-store.json`; `ls` |
| Docker data filesystem   | 1006.9 GB (WSL2 default), 52.9 GB used. `DiskSizeMiB` = 65536 in `settings-store.json` doesn't apply on the WSL2 backend. | `docker run --rm --pull never alpine df -h /` (from Git Bash: `MSYS_NO_PATHCONV=1`) |
| Docker disk in use       | about 47 GB: images 13.1 GB, volumes 12.8 GB, build cache 21.2 GB (all reclaimable) | `docker system df` |
| Free on L:               | 865 GB of 932 GB                                                              | `df -h` |
| Free on C:               | 326 GB of 931 GB                                                              | `df -h` |
| Free on D:               | 1.2 TB of 1.9 TB                                                              | `df -h` |

The `llm-models` volume lives inside `docker_data.vhdx` on L:. The vhdx grows as models are added and doesn't shrink by itself when they're deleted.

In practice the limit is free space on L: (865 GB), not the 1 TB filesystem size, because the vhdx file grows as it fills. That's plenty for 100+ GB of models.

## Notes

- **Driver and CUDA.** Driver 560.94 supports CUDA up to 12.6. If the current llama.cpp CUDA image is built on a newer CUDA, it won't start on this driver. Check the image's CUDA version first. If it's too new, either update the driver (your call) or pin an older image built on CUDA 12.6 or earlier.
- **RAM speed.** The kits are rated 5200 MT/s but run at 4000. Four DDR5 modules often run below their rating, or XMP may simply be off. At 5200 the theoretical bandwidth would be 83 GB/s. Decided 2026-10-29: it stays at 4000 MT/s for the whole series. The post should say the kit is rated higher.
- **Hybrid CPU.** Windows 10 doesn't schedule well for 8 performance + 4 efficiency cores, and inside the WSL2 VM the two kinds of core look identical. Offload thread counts need a quick sweep (for example 8, 12, 16) rather than assuming "physical cores".
- **WSL memory.** 31 GiB is enough for a ~30B MoE at Q4 (about 18 GB) plus the GPU-side buffers. A Q8 MoE (32.5 GB against a 33.6 GB limit) only just fits, with about 1 GB to spare, and may need the limit raised in `.wslconfig`.
- **No integrated GPU.** The desktop's VRAM share can't be moved off the 3060 Ti, so the "with as much closed as possible" measurement is the floor.
