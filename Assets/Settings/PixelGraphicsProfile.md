# Pixel OLED grafisch profiel (URP 17, Unity 6)

Toepassen: **Tools > Generals > Apply Pixel Graphics Profile**. Het menu stelt de instellingen hieronder in.
Het zet ook de Android-instellingen en maakt `Assets/Settings/PixelVolumeProfile.asset` aan.

## Renderer

| Instelling | Waarde | Waarom |
|---|---|---|
| Rendering Path | **Forward** | Forward+ op Android is niet overal stabiel. Forward is de veilige keuze voor een RTS met veel lichten, zolang er maar een directional light is. |
| Render Scale | 1.0 | Het OLED van de Pixel heeft genoeg dichtheid. Schaal niet terug. |
| HDR | aan | Nodig voor Bloom op lasers en explosies. |
| MSAA | uit | Anti-aliasing loopt via FXAA. |

## Schaduwen

| Instelling | Waarde |
|---|---|
| Shadow Distance | 45 m |
| Cascade Count | 1 |
| Main Light Shadow Resolution | 1024 |
| Soft Shadows | uit |

Alleen de directional light werpt schaduwen. Gebouwen en units zonder schaduw-cast blijven goedkoop.

## Anti-aliasing: FXAA of SMAA

**Kies FXAA.** SMAA geeft scherpere randen, maar kost meer bandbreedte per frame. Op een scherm van 400+ ppi valt aliasing bovendien veel minder op. Zet per camera: `Anti-aliasing = Fast Approximate Anti-aliasing`. Dat staat al in `EditorSceneSetup`.

## HDR en Bloom

- Volume-profiel `PixelVolumeProfile`, Bloom: threshold 0.95, intensity 0.6, scatter 0.6.
- Gebruik voor lasers, explosies en mijnen een emissieve kleur met een helderheid boven 1, bijvoorbeeld HDR-kleur `(3, 1, 0.5)`. Anders gloeit er niets.
- Het UI-canvas is Screen Space – Overlay en wordt niet door het Volume-profiel geraakt.

## Android

| Instelling | Waarde |
|---|---|
| Graphics APIs | Vulkan, daarna OpenGL ES 3 |
| Scripting Backend | IL2CPP |
| Architecture | ARM64 |
| Minimum API | 26 |
| Color Space | Linear |
| Orientation | Landscape Left |

## Handmatig in te stellen

- **Render Outside Safe Area** staat op Project Settings > Player > Android > Resolution and Presentation **uit**. Dat is nodig voor `SafeAreaFitter`.
- **Frame rate:** de code zet `Application.targetFrameRate = 60` bij het starten van een scène. Dat houdt de Tensor-chip koel.
