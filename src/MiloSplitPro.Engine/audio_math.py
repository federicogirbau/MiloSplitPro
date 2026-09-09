import numpy as np

def compute_complement_stem(original_audio: np.ndarray, separated_stems: list[np.ndarray]) -> np.ndarray:
    """
    Computes sample-accurate complement (Other) stem:
    Other = Original - Sum(SelectedStems)
    """
    if original_audio is None or len(original_audio) == 0:
        return np.array([], dtype=np.float32)

    if not separated_stems:
        return np.copy(original_audio)

    stem_sum = np.zeros_like(original_audio, dtype=np.float32)
    for stem in separated_stems:
        if stem.shape == original_audio.shape:
            stem_sum += stem
        else:
            # Match lengths if slightly mismatched
            min_len = min(len(stem), len(original_audio))
            stem_sum[:min_len] += stem[:min_len]

    complement = original_audio - stem_sum
    return complement.astype(np.float32)

def calculate_stem_metrics(audio: np.ndarray) -> dict:
    if audio is None or len(audio) == 0:
        return {"peak": 0.0, "rms": 0.0, "is_silent": True}

    peak = float(np.max(np.abs(audio)))
    rms = float(np.sqrt(np.mean(audio ** 2)))
    is_silent = rms < 1e-4 and peak < 1e-3

    return {
        "peak": peak,
        "rms": rms,
        "is_silent": is_silent
    }

def verify_reconstruction(original: np.ndarray, stems: list[np.ndarray]) -> float:
    """Returns the maximum absolute difference across all samples."""
    if not stems or original is None:
        return 0.0

    stem_sum = np.zeros_like(original, dtype=np.float32)
    for stem in stems:
        min_len = min(len(stem), len(original))
        stem_sum[:min_len] += stem[:min_len]

    max_diff = float(np.max(np.abs(original - stem_sum)))
    return max_diff
