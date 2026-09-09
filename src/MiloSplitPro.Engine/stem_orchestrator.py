import os
import sys
import time
import wave
import json
import struct
import numpy as np
import torch

from audio_math import compute_complement_stem, calculate_stem_metrics, verify_reconstruction

def read_audio_file(file_path: str) -> tuple[np.ndarray, int, int]:
    """Reads any supported audio format (MP3, WAV, FLAC, M4A, OGG) into float32 array [-1.0, 1.0]."""
    # 1. Try soundfile
    try:
        import soundfile as sf
        data, samplerate = sf.read(file_path, dtype='float32')
        if data.ndim == 1:
            data = data.reshape(-1, 1)
        return data, samplerate, data.shape[1]
    except Exception:
        pass

    # 2. Try torchaudio
    try:
        import torchaudio
        waveform, samplerate = torchaudio.load(file_path)
        data = waveform.numpy().T.astype(np.float32)
        if data.ndim == 1:
            data = data.reshape(-1, 1)
        return data, samplerate, data.shape[1]
    except Exception:
        pass

    # 3. Try librosa
    try:
        import librosa
        data, samplerate = librosa.load(file_path, sr=None, mono=False)
        if data.ndim == 1:
            data = data.reshape(-1, 1)
        elif data.shape[0] <= 2 and data.ndim == 2:
            data = data.T
        return data.astype(np.float32), samplerate, data.shape[1]
    except Exception:
        pass

    # 4. Fallback to standard wave.open for WAV files
    return read_wav_pcm(file_path)

def read_wav_pcm(file_path: str) -> tuple[np.ndarray, int, int]:
    """Reads a WAV file into float32 array in [-1.0, 1.0], sample_rate, channels."""
    with wave.open(file_path, "rb") as wf:
        n_channels = wf.getnchannels()
        sampwidth = wf.getsampwidth()
        framerate = wf.getframerate()
        n_frames = wf.getnframes()
        raw_bytes = wf.readframes(n_frames)

    if sampwidth == 2: # 16-bit PCM
        audio_int16 = np.frombuffer(raw_bytes, dtype=np.int16)
        audio = audio_int16.astype(np.float32) / 32768.0
    elif sampwidth == 3: # 24-bit PCM
        total_samples = len(raw_bytes) // 3
        audio = np.zeros(total_samples, dtype=np.float32)
        for i in range(total_samples):
            b = raw_bytes[i*3 : (i+1)*3]
            val = struct.unpack("<i", b + (b'\xff' if b[2] & 0x80 else b'\x00'))[0]
            audio[i] = val / 8388608.0
    elif sampwidth == 4: # 32-bit float or int
        audio = np.frombuffer(raw_bytes, dtype=np.float32)
    else:
        raise ValueError(f"Unsupported sample width: {sampwidth} bytes")

    if n_channels > 1:
        audio = audio.reshape(-1, n_channels)
    else:
        audio = audio.reshape(-1, 1)

    return audio, framerate, n_channels

def save_audio_stem(file_base_path: str, audio: np.ndarray, sample_rate: int, fmt: str = "mp3", bits_per_sample: int = 24) -> str:
    """Saves audio array to target format (MP3, WAV, FLAC, OGG). Returns actual file path."""
    os.makedirs(os.path.dirname(file_base_path), exist_ok=True)
    fmt_lower = fmt.lower().strip()
    
    if fmt_lower == "mp3":
        target_path = file_base_path if file_base_path.lower().endswith(".mp3") else f"{file_base_path}.mp3"
        try:
            import lameenc
            encoder = lameenc.Encoder()
            encoder.set_bit_rate(320)
            encoder.set_in_sample_rate(sample_rate)
            n_ch = audio.shape[1] if audio.ndim > 1 else 1
            encoder.set_channels(n_ch)
            encoder.set_quality(2) # High quality VBR/CBR
            clipped = np.clip(audio, -1.0, 1.0)
            int_data = (clipped * 32767.0).astype(np.int16)
            mp3_data = encoder.encode(int_data.tobytes()) + encoder.flush()
            with open(target_path, "wb") as f:
                f.write(mp3_data)
            return target_path
        except Exception:
            import soundfile as sf
            sf.write(target_path, audio, sample_rate, format="MP3")
            return target_path
            
    elif fmt_lower == "flac":
        target_path = file_base_path if file_base_path.lower().endswith(".flac") else f"{file_base_path}.flac"
        import soundfile as sf
        sf.write(target_path, audio, sample_rate, format="FLAC")
        return target_path
        
    elif fmt_lower == "ogg":
        target_path = file_base_path if file_base_path.lower().endswith(".ogg") else f"{file_base_path}.ogg"
        import soundfile as sf
        sf.write(target_path, audio, sample_rate, format="OGG")
        return target_path
        
    else: # WAV (default lossless uncompressed)
        target_path = file_base_path if file_base_path.lower().endswith(".wav") else f"{file_base_path}.wav"
        write_wav_pcm(target_path, audio, sample_rate, bits_per_sample=bits_per_sample)
        return target_path

class StemOrchestrator:
    def __init__(self, progress_callback=None):
        self.progress_callback = progress_callback or (lambda data: None)
        self.is_cancelled = False

    def emit_progress(self, stage: str, progress: float, message: str, stem: str = None, model: str = None, device: str = None):
        self.progress_callback({
            "stage": stage,
            "progress": round(progress, 2),
            "message": message,
            "stem": stem,
            "model": model,
            "device": device
        })

    def separate(
        self,
        input_file: str,
        output_dir: str,
        selected_stems: list[str],
        device: str = "auto",
        generate_complement: bool = True,
        output_format: str = "mp3",
        models_dir: str = None
    ) -> dict:
        start_time = time.time()
        self.emit_progress("Initializing", 0.0, "Iniciando motor de separación...")

        if not os.path.exists(input_file):
            raise FileNotFoundError(f"El archivo de entrada no existe: {input_file}")

        self.emit_progress("ValidatingFile", 5.0, f"Cargando archivo de audio: {os.path.basename(input_file)}")
        original_audio, sample_rate, channels = read_audio_file(input_file)
        duration_sec = len(original_audio) / sample_rate

        # Ensure stereo audio for Demucs
        if original_audio.shape[1] == 1:
            stereo_audio = np.repeat(original_audio, 2, axis=1)
        else:
            stereo_audio = original_audio[:, :2]

        # Determine target device
        resolved_device = "cpu"
        device_name = "CPU"
        if device.lower() in ["auto", "cuda"] and torch.cuda.is_available():
            resolved_device = "cuda"
            device_name = f"CUDA: {torch.cuda.get_device_name(0)}"
        else:
            device_name = "CPU (Modo seguro)"

        # Choose model: HTDemucs 6s if Guitar or Piano requested, else HTDemucs 4s
        needs_6s = any(s in ["Guitar", "Piano"] for s in selected_stems)
        model_name = "htdemucs_6s" if needs_6s else "htdemucs"

        self.emit_progress("LoadingModel", 12.0, f"Cargando modelo neuronal {model_name}...", model=model_name, device=device_name)

        import demucs.pretrained
        import demucs.apply

        model = demucs.pretrained.get_model(model_name)
        model.to(resolved_device)
        model.eval()

        # Resample to 44.1kHz if needed by model
        target_sr = model.samplerate
        audio_tensor = torch.from_numpy(stereo_audio.T).float() # [2, samples]
        
        if sample_rate != target_sr:
            try:
                import torchaudio.transforms as T
                resampler = T.Resample(sample_rate, target_sr)
                audio_tensor = resampler(audio_tensor)
            except Exception:
                pass

        audio_tensor = audio_tensor.unsqueeze(0).to(resolved_device) # [1, 2, samples]

        # Normalize audio tensor for Demucs
        ref = audio_tensor.mean(0)
        audio_tensor_norm = (audio_tensor - ref.mean()) / (ref.std() + 1e-8)

        self.emit_progress("SeparatingAudio", 20.0, "Procesando separación con redes neuronales...", model=model_name, device=device_name)

        if self.is_cancelled:
            self.emit_progress("Cancelled", 0.0, "Separación cancelada.")
            return {"success": False, "cancelled": True}

        # Apply Demucs Model with AI Inference
        with torch.no_grad():
            sources = demucs.apply.apply_model(
                model,
                audio_tensor_norm,
                device=resolved_device,
                shifts=1,
                split=True,
                overlap=0.25,
                progress=False
            )
            sources = sources * (ref.std() + 1e-8) + ref.mean()

        self.emit_progress("SeparatingAudio", 80.0, "Organizando y exportando pistas separadas...", model=model_name, device=device_name)

        # Map and resample Demucs sources back to original sample rate if necessary
        # sources shape: [1, num_sources, 2, samples]
        model_sources_map = {}
        out_resampler = None
        if sample_rate != target_sr:
            try:
                import torchaudio.transforms as T
                out_resampler = T.Resample(target_sr, sample_rate)
            except Exception:
                out_resampler = None

        for idx, src_name in enumerate(model.sources):
            stem_tensor = sources[0, idx].cpu() # [2, samples]
            
            if out_resampler is not None:
                try:
                    stem_tensor = out_resampler(stem_tensor)
                except Exception:
                    pass
            elif sample_rate != target_sr:
                try:
                    import scipy.signal
                    import math
                    gcd = math.gcd(target_sr, sample_rate)
                    up = sample_rate // gcd
                    down = target_sr // gcd
                    raw_np = stem_tensor.numpy().T
                    raw_resampled = scipy.signal.resample_poly(raw_np, up, down, axis=0).astype(np.float32)
                    stem_tensor = torch.from_numpy(raw_resampled.T)
                except Exception:
                    pass

            stem_np = stem_tensor.numpy().T # [samples, 2]

            # Match exact original length
            if len(stem_np) != len(original_audio):
                if len(stem_np) > len(original_audio):
                    stem_np = stem_np[:len(original_audio)]
                else:
                    pad = np.zeros((len(original_audio) - len(stem_np), stem_np.shape[1]), dtype=np.float32)
                    stem_np = np.vstack([stem_np, pad])

            model_sources_map[src_name.lower()] = stem_np

        os.makedirs(output_dir, exist_ok=True)
        separated_stems = {}
        processed_stems_audio = []

        # Mapping requested categories to extracted stems
        for stem_name in selected_stems:
            if self.is_cancelled:
                self.emit_progress("Cancelled", 0.0, "Separación cancelada.")
                return {"success": False, "cancelled": True}

            stem_audio = None

            if stem_name == "Vocals" and "vocals" in model_sources_map:
                stem_audio = model_sources_map["vocals"]
            elif stem_name == "LeadVocals" and "vocals" in model_sources_map:
                # Center vocal extraction
                voc = model_sources_map["vocals"]
                center = (voc[:, 0] + voc[:, 1]) / 2.0
                stem_audio = np.column_stack([center, center])
            elif stem_name == "BackingVocals" and "vocals" in model_sources_map:
                # Side / backing vocal extraction
                voc = model_sources_map["vocals"]
                side = (voc[:, 0] - voc[:, 1]) / 2.0
                stem_audio = np.column_stack([side, -side])
            elif stem_name == "Drums" and "drums" in model_sources_map:
                stem_audio = model_sources_map["drums"]
            elif stem_name == "Bass" and "bass" in model_sources_map:
                stem_audio = model_sources_map["bass"]
            elif stem_name == "Guitar" and "guitar" in model_sources_map:
                stem_audio = model_sources_map["guitar"]
            elif stem_name == "Piano" and "piano" in model_sources_map:
                stem_audio = model_sources_map["piano"]
            elif stem_name == "Other" and "other" in model_sources_map:
                stem_audio = model_sources_map["other"]
            else:
                # Fallback to general stem if available
                stem_audio = model_sources_map.get(stem_name.lower(), model_sources_map.get("other", stereo_audio))

            stem_base_path = os.path.join(output_dir, stem_name)
            stem_path = save_audio_stem(stem_base_path, stem_audio, sample_rate, fmt=output_format)
            metrics = calculate_stem_metrics(stem_audio)

            separated_stems[stem_name] = {
                "category": stem_name,
                "stemName": stem_name,
                "filePath": stem_path,
                "duration": duration_sec,
                "sampleRate": sample_rate,
                "channels": 2,
                "peakAmplitude": metrics["peak"],
                "rmsEnergy": metrics["rms"]
            }
            processed_stems_audio.append(stem_audio)

        # Complement Logic (Other)
        if generate_complement and "Other" not in selected_stems:
            self.emit_progress("CalculatingComplement", 92.0, "Calculando complemento de mezcla (Other)...")
            if "other" in model_sources_map and len(selected_stems) >= 3:
                complement_audio = model_sources_map["other"]
            else:
                complement_audio = compute_complement_stem(stereo_audio, processed_stems_audio)

            comp_metrics = calculate_stem_metrics(complement_audio)

            other_base_path = os.path.join(output_dir, "Other")
            other_path = save_audio_stem(other_base_path, complement_audio, sample_rate, fmt=output_format)
            separated_stems["Other"] = {
                "category": "Other",
                "stemName": "Other",
                "filePath": other_path,
                "duration": duration_sec,
                "sampleRate": sample_rate,
                "channels": 2,
                "peakAmplitude": comp_metrics["peak"],
                "rmsEnergy": comp_metrics["rms"]
            }

        elapsed = time.time() - start_time
        self.emit_progress("Completed", 100.0, f"Separación neuronal completada en {elapsed:.1f}s.", device=device_name)

        return {
            "success": True,
            "outputDirectory": output_dir,
            "stems": list(separated_stems.values()),
            "processingTime": elapsed,
            "deviceUsed": device_name
        }
