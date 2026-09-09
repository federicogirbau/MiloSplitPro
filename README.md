# Milo Split Pro 🎵⚡

**Milo Split Pro** es una aplicación de escritorio nativa para Windows desarrollada en **.NET 8 LTS (WPF / MVVM)** que integra separación musical multipista local con Inteligencia Artificial utilizando **Meta AI Demucs v4 (HTDemucs)**, reproducción de mezcla sample-accurate con **NAudio**, exportación multipistas (MP3 320k, WAV 24-bit, FLAC) y cumplimiento estricto de licencias LGPL y SBOM.

![Milo Split Pro Icon](src/MiloSplitPro.App/Assets/app_icon.png)

---

## Características Principales

- 🧠 **Separación con IA 100% Local y Privada**: Sin envíos a servidores externos ni costos por minuto. Utiliza los modelos neuronales `htdemucs` (4 pistas) y `htdemucs_6s` (6 pistas).
- 🎛️ **Matemática Residual Exacta (Complement Other)**: Deducción espectral y de muestras para aislamiento preciso de instrumentos residuales (`Other = Original - Σ(Pistas)`).
- 🔊 **Reproductor y Mixer Multicanal**: Control de volumen individual por fader, modos **MUTE** y **SOLO** mutuamente exclusivos con estilos diferenciados, y desplazamiento rápido por la línea de tiempo.
- 💾 **Exportación Offline Multipista**: Generación de stems en **MP3 (320 kbps)**, **WAV PCM** o **FLAC**, además de exportación directa de la mezcla personalizada.
- 🎨 **Interfaz Fluent Dark**: Diseño moderno con WindowChrome, barras de desplazamiento fluidas y tema oscuro optimizado.
- 📜 **Auditoría de Licencias y SBOM**: Verificación fail-closed de licencias y generación de SBOM CycloneDX.

---

## Arquitectura de la Solución

```
Milo Split Pro/
├── src/
│   ├── MiloSplitPro.Core/          # DTOs, Enums, AudioMath (residuo exacto), ManifestValidator
│   ├── MiloSplitPro.Engine/        # Meta AI Demucs v4, lameenc 320k, stream IPC JSON Lines
│   ├── MiloSplitPro.App/           # Interfaz WPF .NET 8, NAudio AudioEngine, MVVM
│   └── MiloSplitPro.Tests/         # Suite de pruebas automatizadas con xUnit y FluentAssertions
├── installer/                      # Script Inno Setup para empaquetado e instalador
├── bom.cyclonedx.json              # Manifiesto de Software Bill of Materials (SBOM)
└── THIRD_PARTY_NOTICES.txt        # Avisos de licencias de terceros
```

---

## Requisitos de Ejecución

- **Sistema Operativo**: Windows 10 / 11 (64-bit)
- **.NET Runtime**: .NET 8.0 Desktop Runtime x64
- **Python**: 3.10+ con `demucs`, `torch`, `torchaudio`, `soundfile`, `lameenc`
- **Aceleración**: GPU NVIDIA compatible con CUDA (recomendado) o CPU multi-core.

---

## Compilación y Pruebas

```powershell
# Ejecutar suite de pruebas unitarias
dotnet test

# Compilar y publicar versión final
dotnet publish src/MiloSplitPro.App/MiloSplitPro.App.csproj -c Release -r win-x64 --self-contained false -o build/publish
```

---

## Licencia

Este proyecto está distribuido bajo las licencias correspondientes a sus componentes de código abierto. Consulta [`THIRD_PARTY_NOTICES.txt`](THIRD_PARTY_NOTICES.txt) y [`bom.cyclonedx.json`](bom.cyclonedx.json) para más detalles.
