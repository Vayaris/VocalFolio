# Third-party components and models

The MIT license at the repository root applies to VocalFolio's own code only.

| Component | Upstream / license | Distribution |
|---|---|---|
| NAudio 2.2.1 | https://github.com/naudio/NAudio — MIT, Mark Heath | Managed libraries in the EXE; license retained |
| .NET / Windows Desktop 10 | https://github.com/dotnet — MIT and bundled notices | Self-contained runtime; licenses under `licenses/` |
| NeMo-Speech.cpp 0.2.0+nightly.66d50d9 | https://github.com/NVIDIA/NeMo-Speech.cpp/tree/66d50d9 — Apache-2.0 and upstream third-party notices | Unmodified open-source runtime components embedded in `src/Assets/runtime.zip`; notices preserved inside archive |
| ggml, llama.cpp, cpp-httplib, miniaudio, vcpkg libraries | Upstream notices in the runtime distribution | Their individual licenses remain in the archive and extracted runtime |
| NVIDIA Nemotron 3.5 ASR 0.6B | https://huggingface.co/nvidia/nemotron-3.5-asr-streaming-0.6b — OpenMDW 1.1 | Weights downloaded by the user, pinned revision/hash; model card and license retained |
| NVIDIA Parakeet TDT 0.6B v3 | https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3 — CC BY 4.0 | Attribution: NVIDIA; unmodified weights downloaded separately |
| NVIDIA Streaming Sortformer 4spk v2 | https://huggingface.co/nvidia/diar_streaming_sortformer_4spk-v2 — CC BY 4.0 | Attribution: NVIDIA; unmodified weights downloaded separately |

The upstream notice document discusses optional features and platforms too. The supplied CPU engine reports `flashlight=false`, `text_normalization=false`, `backend_cuda=false`; this package does not add KenLM/Flashlight or CUDA binaries. Do not assume this notice covers a different engine you substitute.

## Microsoft Visual C++ runtime

The NVIDIA archive contains proprietary Microsoft VC runtime libraries. They are not relicensed as MIT and are **excluded from VocalFolio's embedded runtime and source repository**. The app uses Windows-provided libraries when available; if missing, it retrieves the pinned official NVIDIA release archive directly on the user's machine, verifies SHA-256 and extracts the required files locally. That download is not a grant of extra redistribution rights.

Official rules: https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files . Anyone redistributing those Microsoft files separately must verify the applicable Microsoft terms and rights.

The original vendor archive SHA-256 is `a43f373bd78b2b4ab90167bc838c6c4c7f1a0ae526bfb3bc237e46b01165c8de`, GitHub release asset `686263624`. If the upstream asset is withdrawn or changes, the app fails safely and requires an updated release; it never accepts an unverified replacement. File-level hashes of excluded dependencies are recorded in `src/Assets/vendor-runtime.json`.

## Marks and data

NVIDIA and Microsoft names identify their components and do not imply affiliation or endorsement. The project claims no rights over participant voices, meetings, transcripts supplied by users, or third-party trademarks. Public test clips are fetched for developer validation and are not bundled in releases.
