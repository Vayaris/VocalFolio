# Validation

For v1.0.1, the icon update was compiled successfully. All ten ICO frames (16–256 px) were decoded, transparency was checked, and the icon was extracted from the published EXE to confirm native resource embedding. The audio engine is unchanged; the transcription suite was not repeated for this visual-only update.

The underlying application passed 25 automated checks before this first public release: NVIDIA result parsing, French reference transcript, speaker IDs, exports, runtime extraction/repair, model migration/import integrity, HTTP Range resume, native progress, cancellation, Parakeet and bounded WASAPI playback. Real playback was captured through loopback with the expected duration. Moving the single EXE and completing a fresh transcription were also checked.

The public release adds VocalFolio branding, version 1.0.0, an explicit recording authorization reminder, MIT licensing and privacy/third-party documentation. Proprietary Microsoft VC DLLs are excluded from the embedded package; missing dependencies are obtained directly from the verified upstream asset.

No guarantee of transcription or speaker accuracy is made. Two-hour meetings, an annotated four-speaker benchmark, CUDA and Bluetooth profile changes have not been validated end to end. ETA is approximate. No personal recordings or transcripts are distributed with the repository or release.

Run `verify.ps1` with .NET 10 on Windows to reproduce checks using public test clips. `-Playback` plays a short clip; `-Capture` briefly records from default devices locally. Downloaded test data and model files are ignored by Git.
