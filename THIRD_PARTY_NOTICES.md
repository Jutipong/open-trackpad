# Third-party notices

OpenTrackpad itself is MIT-licensed (see [LICENSE](LICENSE)). The installer
bundles the third-party driver package listed below.

## Magic Trackpad 2 Precision Touchpad driver v2.3

- Project: [vitoplantamura/MagicTrackpad2ForWindows](https://github.com/vitoplantamura/MagicTrackpad2ForWindows)
- License: GNU General Public License, version 2 (see [licenses/GPL-2.0.txt](licenses/GPL-2.0.txt))
- Bundled files: the unmodified Microsoft-signed AMD64 driver package
  (`AmtPtpDevice.inf`, `amtptpdevice.cat`, `AmtPtpDeviceUsbUm.dll`,
  `AmtPtpHidFilter.sys`)

The driver is a separate program installed by the OpenTrackpad installer with
`pnputil` and is not linked with the OpenTrackpad application. The driver
binaries are redistributed unmodified under GPL-2.0.

### Source code

The complete corresponding source code is available from the upstream project
at the release matching v2.3:

<https://github.com/vitoplantamura/MagicTrackpad2ForWindows>

Written offer: the OpenTrackpad maintainer will provide the corresponding
source for the bundled driver package on request through
<https://github.com/Jutipong/open-trackpad/issues> for at least three years
after the release date.
