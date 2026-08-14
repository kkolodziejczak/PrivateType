# Public-release obligations

Research date: 2026-08-14. This is an engineering release checklist, not legal advice.

## Release decision summary

Do not publish the current portable ZIP yet. The release build copies native
NeMo-Speech.cpp binaries but does not bundle the upstream `LICENSE`, `NOTICE`,
or third-party notices. The chosen distribution route and signing identity also
remain undecided.

## Model: NVIDIA Nemotron 3.5 ASR Streaming 0.6B

The selected model is governed by OpenMDW-1.1, according to NVIDIA's official
[model card](https://huggingface.co/nvidia/nemotron-3.5-asr-streaming-0.6b).
The official [OpenMDW-1.1 text](https://raw.githubusercontent.com/OpenMDW/OpenMDW/refs/heads/main/1.1/LICENSE.OpenMDW-1.1)
requires a distributor of any part of the Model Materials to retain the
agreement and applicable copyright/origin notices, and places responsibility
for required third-party clearances on that distributor.

The current `Build-PortableRelease.ps1` deliberately excludes `models/`; the
user downloads and verifies the model after installation. On that design, the
application ZIP is not a model redistribution. Before release, retain the
pinned model revision and checksum, and make the model name, source, and
OpenMDW-1.1 terms available in the first-run/download documentation. Reassess
this requirement before ever bundling a model file or hosting a mirror.

## Native runtime notices

NVIDIA's official [NeMo-Speech.cpp license section](https://github.com/NVIDIA/NeMo-Speech.cpp#license)
licenses NVIDIA-authored code under Apache-2.0, and its upstream
[NOTICE](https://github.com/NVIDIA/NeMo-Speech.cpp/blob/main/NOTICE) directs
distributors to the third-party notices. The upstream
[third-party notices](https://github.com/NVIDIA/NeMo-Speech.cpp/blob/main/THIRD_PARTY_NOTICES.md)
list components with MIT and BSD-3-Clause terms and identify KenLM as primarily
LGPL-2.1-or-later (with specific source allowlisting and exclusions).

Therefore the release must not describe its runtime as simply Apache-2.0.
Before publishing:

1. Audit the exact native DLLs and executable copied by `Build-PortableRelease.ps1`, including their link configuration.
2. Include the upstream `LICENSE`, `NOTICE`, and a complete, readable third-party-notice/license bundle covering every distributed component.
3. Obtain a legal review of the exact KenLM/LGPL footprint and any other copyleft component in the shipped binary set.

## Windows distribution and signing

Microsoft's [code-signing guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)
recommends Microsoft Store/MSIX for most new Windows apps; the Store signs and
distributes the package. For a non-Store path, Microsoft recommends Azure
Artifact Signing. Microsoft's [distribution-path guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/choose-distribution-path)
describes MSIX direct distribution with a CA-trusted signature and `.appinstaller`
updates. Its [SmartScreen guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)
states that every binary and code path must be signed consistently and must not
be modified after signing; new signed files may still need time to earn
reputation.

Choose one route before packaging:

| Option | Owner decision | Release implication |
|---|---|---|
| Microsoft Store MSIX | Store publisher account and listing | Store signing, discovery, and updates. |
| Direct MSIX | Certificate/Artifact Signing identity and update host | Sign package and publish an `.appinstaller` feed. |
| Direct ZIP | Signing identity and manual-update policy | Sign every executable/DLL, publish checksums, and explain updates clearly. |

## Hard publication blockers

- [ ] Select Store MSIX, direct MSIX, or direct ZIP; select the publisher/signing identity.
- [ ] Produce and verify a release from a clean checkout after the current UI changes are committed.
- [ ] Audit the exact native binary dependency/license footprint and bundle required notices/licenses.
- [ ] Complete legal review of the notices bundle and LGPL/KenLM implications.
- [ ] Sign the final, immutable artifacts and verify signatures before distribution.
- [ ] Run the documented clean-machine acceptance test on the final signed artifact.

## Evidence reviewed in this repository

- `Build-PortableRelease.ps1` copies `nemo-speech.exe` and seven native DLLs
  into `engine/bin`, but does not copy license or notice files.
- `MODEL_ARTIFACT.md` pins the downloaded model revision and SHA-256.
- The checked-out NeMo-Speech.cpp source includes `LICENSE`, `NOTICE`, and
  `THIRD_PARTY_NOTICES.md`; those are the source-of-truth notice materials to
  reconcile with the actual shipped build.
