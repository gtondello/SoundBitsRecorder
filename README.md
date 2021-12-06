# SoundBits Recorder

![GamefulBits](SoundBitsRecorder/GamefulBitsLogo.png)

Author: Gustavo Fortes Tondello
Company: GamefulBits
Website: [https://gamefulbits.com/SoundBitsRecorder](https://gamefulbits.com/SoundBitsRecorder)
Source code: [https://github.com/gtondello/SoundBitsRecorder](https://github.com/gtondello/SoundBitsRecorder)

Copyright 2021 Gustavo Fortes Tondello

Licensed under the Apache License, Version 2.0. See [LICENSE.txt](LICENSE.txt) for details.


## Description

SoundBits Recorder is an open-source audio recorder that can merge two audio samples into one and save it to an MP3 file.

This is useful to merge audio from an input device (e.g., a microphone) with an output device (e.g., the sound being played).

For example, this software can be used to record audio from online meetings, saving what the user is speaking on their mic and what they're hearing from others,
or to sing along a background recording.

This software is officially tested and supported on Windows 10.
(It may work on Windows 7 or 8, but I have not tested and cannot guarantee it.)


## Building and running

Just open the project on Visual Studio 2019 and build it normally.


### Versioning

If you want to build a new version, the Assembly version must be updated accordingly (on Visual Studio, go to Project -> SoundBits Recorder properties, and click on "Assembly Information...".

The build date is captured automatically into the file `Properties/BuildDate.txt` and is displayed in the About window, together with the Version.


### Automatic Updates

This software uses [AutomaticUpdater.NET](https://github.com/ravibpatel/AutoUpdater.NET) to automatically detect and download new versions.
Currently, the AutoUpdater is pointed to the installation packages I distribute on my website.

If you want to distribute your our builds, you need to upload an installation package and an updated copy of `updates/SoundBitsRecorder.xml` on your own website.
Then, update the AutoUpdater URL in the constructor of the `RecordingWindow`.


### Implementation Notes

This software uses the [NAudio](https://github.com/naudio/NAudio) package to capture audio and the [LAME MP3 Encoder](https://lame.sourceforge.io/) to write MP3 files.


## Building the Installation Package

Coming soon...


## Contributing

Please feel free to send issues or suggestions.

Please feel free to submit Pull Requests with suggested improvements.
I would appreciate if any suggestion is properly tested and documented following the documentation format of the existing classes.

If you would like to provide a new translation, feel free to only submit a translated copy of `Properties/Resources.resx`, which I can then add to the application.
