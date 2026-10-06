# Embedded fonts

These files are compiled into the assembly as `EmbeddedResource` (see `SoftCo.csproj`) and loaded
by `Services/Pdf/EmbeddedFontResolver.cs`.

## Why they are here rather than installed

PDFsharp 6 on .NET Core has no access to system fonts and throws unless a font resolver is set.
Shipping the bytes inside the assembly means a purchase order renders in the real brand faces
identically on a developer's Windows machine and in the Linux container, and the Dockerfile needs
no font package installed. It also removes the classic failure where a PDF feature passes locally
and breaks in production on font resolution.

## Files

| File | Family | Weight | Used for |
|---|---|---|---|
| `Manrope-Regular.ttf` | Manrope | 400 | Body text, labels, and every figure |
| `Manrope-SemiBold.ttf` | Manrope | 600 | Bold body text |
| `CormorantGaramond-Regular.ttf` | Cormorant Garamond | 400 | Headings and the wordmark |

Italic is simulated rather than shipped — nothing in these documents is italic. Cormorant's bold is
simulated too, because it appears only as headings at a size where the synthesised weight is
indistinguishable.

These are **static instances**, not the variable-axis files published in `google/fonts`. The
variable files are valid TrueType but render at their axis default, which for Manrope is
ExtraLight — far too thin for body text. The static instances come from the Google Fonts API,
which serves per-weight TrueType to clients that ask for it.

## Licence

Both families are **SIL Open Font Licence 1.1**, which expressly permits embedding in documents and
redistribution. The licence text for each is alongside the fonts, as the OFL requires:

- `OFL-Manrope.txt` — Copyright 2018 The Manrope Project Authors
- `OFL-CormorantGaramond.txt` — Copyright 2015 The Cormorant Project Authors

Neither may be sold on its own. Nothing here does that.

## Cremore

Cremore is Soft & Co's actual display face and is **not** licensed for this project, on screen or in
print. Cormorant Garamond carries the display role in both places, exactly as it does in
`wwwroot/css/site.css`. If Cremore is licensed later, drop the `.ttf` in here and change the one
mapping in `EmbeddedFontResolver` and the one constant in `PdfBrand` — nothing else refers to it.
