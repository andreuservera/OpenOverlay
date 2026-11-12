# Track data: turn names

`iracing/*.json` are copied unmodified from **Lovely Track Data**
(https://github.com/Lovely-Sim-Racing/lovely-track-data, commit 10cab85, 2026-11-12),
© 2025 by [Lovely Sim Racing](https://lsr.gg), licensed under
[CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/). Their corner names come from
[Racing Circuits](https://www.racingcircuits.info).

This licence covers these data files only, not OpenOverlay's code (MIT). It allows sharing them
for non-commercial use with credit; changes to the files must be shared under the same licence.
To update, copy the newer files over these and change the commit above.

Each file is named after iRacing's `WeekendInfo.TrackName` (spaces as dashes) and lists named turns
as fractions of the lap (`start`, `end`). The coach uses them to name the corners it detects
(`Corners/TrackTurnNames`); a name typed into a saved corner map wins over them.
