# Deployment dependency notices

This PRE-H0 snapshot distributes native analysis adapters and a minimal shared Qt runtime. This notice does not change the license of RE_Ecosystem source.

- DIE: MIT; source https://github.com/horsicq/die_library at the supplied ref in `../artifacts.json`. The NOMINMAX adjustment is shown in `scripts/dev/Build-Native-Deps.ps1`; submodules are pinned by the upstream source tree and obtained using `git submodule update --init --recursive`.
- Binwalk: MIT; source https://github.com/ReFirmLabs/binwalk at the supplied ref. The C ABI additions and Windows source adjustment are included in the same developer build script. The binary has not been independently rebuilt for this publication.
- Qt 6.11.1 shared libraries/plugins: open-source LGPL/GPL alternatives are recorded per component in the manufacturer SPDX documents. GPL/LGPL texts and the vendor notice are included. Every published Qt file was matched by SHA256 to the installed vendor SDK. Third-party component names, license texts/references and exact source revisions are in those SPDX documents. Corresponding source is available at the upstream revisions (replace `git://` with `https://`) and https://download.qt.io/archive/qt/6.11/6.11.1/single/ . Libraries remain replaceable shared files; nothing in this distribution restricts replacing or debugging modified Qt libraries. No Qt binary modification is claimed.

Optional translations, QML debugging plugins, the software OpenGL bundle, unverified Microsoft deployment files and the old bundled analyzer DLL remain outside Git. The supported WinUI analyzer is restored through its pinned NuGet package. Held files are not required by the tested headless triage lane.

Manufacturer SPDX documents contain component attribution and dependency relationships. `NOASSERTION` fields do not prove licensing; named shared Qt modules explicitly record LGPL/GPL alternatives. Full build provenance and bit-for-bit reproduction remain verification gaps. Supplied native source refs and measured hashes are deliberately distinguished.
