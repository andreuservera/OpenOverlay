# Car brand logos

Monochrome 24×24 SVGs, one `<path>` each. The logos remain trademarks of their owners; they're
shown only to identify the make of each car.

| File | Source | Licence |
|---|---|---|
| `mercedes.svg` | [Lineicons](https://lineicons.com) | [MIT](https://github.com/LineiconsHQ/Lineicons/blob/main/LICENSE.md) |
| `dallara.svg` | Wikimedia Commons, [Dallara logo.svg](https://commons.wikimedia.org/wiki/File:Dallara_logo.svg) | Public domain |
| `ligier.svg` | Wikimedia Commons, [Logo Ligier.svg](https://commons.wikimedia.org/wiki/File:Logo_Ligier.svg) (the word only) | Public domain |
| `lotus.svg` | Wikipedia, [Lotus Cars logo.svg](https://en.wikipedia.org/wiki/File:Lotus_Cars_logo.svg) (2022 revision) | Public domain |
| `pontiac.svg` | Wikimedia Commons, [Pontiac dart emblem.svg](https://commons.wikimedia.org/wiki/File:Pontiac_dart_emblem.svg) | Public domain |
| `ruf.svg` | Wikimedia Commons, [Ruf Automobile logo.svg](https://commons.wikimedia.org/wiki/File:Ruf_Automobile_logo.svg) | Public domain |
| `skipbarber.svg` | Wikimedia Commons, [Skip Barber Racing School logo.svg](https://commons.wikimedia.org/wiki/File:Skip_Barber_Racing_School_logo.svg) (the two stripes only) | Public domain |
| `williams.svg` | The Williams Racing "W" monogram, redrawn from its three plain shapes | — |
| `radical.svg` | The Radical Sportscars plate and road, redrawn by hand | — |
| `iracing.svg` | The emblem from iRacing's own site logo (iracing.com); the placeholder for any make without a logo here | iRacing trademark |
| `mclaren.svg` | [Simple Icons](https://simpleicons.org) v16.34.0, the speedmark only | [CC0 1.0](https://github.com/simple-icons/simple-icons/blob/develop/LICENSE.md) |
| every other file | [Simple Icons](https://simpleicons.org) v16.34.0 | [CC0 1.0](https://github.com/simple-icons/simple-icons/blob/develop/LICENSE.md) |

The multi-colour sources were reduced to one ink: the coloured parts kept, the white parts cut
out, and the result scaled to fit the 24×24 box.

The file name is the brand's logo key in `ViewModels/CarBrand.cs`. To add one, drop a
single-path SVG here and point the brand's entry at its name; `fill-rule="evenodd"` is honoured.
Each logo is fitted to the cell; `data-scale="0.8"` on the `<svg>` draws one at 80% of that.
A brand whose logo is missing or unreadable shows its monogram instead.
