# Third-party notices

## Programs DiscRippa runs

DiscRippa runs these as separate programs and doesn't modify them.

| Program | Used for | License | In the release zip? |
| --- | --- | --- | --- |
| [MakeMKV](https://www.makemkv.com/) | Reading and ripping DVDs and Blu-rays | Proprietary; free to use while in beta (see [makemkv.com](https://www.makemkv.com/)) | No: install it yourself |
| [HandBrakeCLI](https://handbrake.fr/) | Making the smaller copies | GPL-2.0 | Yes: the unmodified official Windows build, in `tools` |
| [cyanrip](https://github.com/cyanreg/cyanrip) | Ripping music CDs to FLAC | LGPL-2.1. The Windows build also includes libcdio-paranoia (GPL-3.0-or-later) and FFmpeg | Yes: the unmodified official Windows build, in `tools` |

The release zip's `tools` folder holds these programs' license texts (`tools/licenses`) and `tools/SOURCES.txt`. That
file names the exact versions included and links to the official binaries and to their complete source code.

"Activate Blu-ray" downloads the free beta key that MakeMKV's author publishes on the
[MakeMKV forum](https://forum.makemkv.com/forum/viewtopic.php?t=1053). It installs the key where MakeMKV's own
Register window would put it.

Music CD details may be looked up on [MusicBrainz](https://musicbrainz.org/) (through cyanrip) when a disc has no names
stored on it. MusicBrainz data is available under [CC0 and CC BY-NC-SA](https://musicbrainz.org/doc/About/Data_License).

## Libraries included in DiscRippa.exe

| Package | License |
| --- | --- |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MIT |
| [Microsoft.Extensions.DependencyInjection](https://github.com/dotnet/runtime) | MIT |
| [Microsoft.Toolkit.Uwp.Notifications](https://github.com/CommunityToolkit/WindowsCommunityToolkit) | MIT |
| [.NET runtime and WPF](https://github.com/dotnet) (self-contained build) | MIT |

Used only to run the tests: [xUnit](https://github.com/xunit/xunit) (Apache-2.0) and
[Microsoft.NET.Test.Sdk](https://github.com/microsoft/vstest) (MIT).
