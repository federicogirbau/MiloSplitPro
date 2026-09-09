import hashlib
import json
import os
import sys

def compute_sha256(file_path: str) -> str:
    sha = hashlib.sha256()
    with open(file_path, "rb") as f:
        for chunk in iter(lambda: f.read(65536), b""):
            sha.update(chunk)
    return sha.hexdigest().lower()

def validate_models_manifest(manifest_path: str, base_dir: str = None) -> tuple[bool, str, list]:
    if not os.path.exists(manifest_path):
        return False, f"Manifest file not found: {manifest_path}", []

    try:
        with open(manifest_path, "r", encoding="utf-8") as f:
            manifest_data = json.load(f)

        models = manifest_data.get("models", [])
        if not models:
            return False, "No models defined in manifest", []

        validated = []
        for m in models:
            req_fields = ["id", "license", "sha256", "capabilities"]
            for field in req_fields:
                if not m.get(field):
                    return False, f"Model {m.get('name', 'Unknown')} missing required field: {field}", []

            if base_dir and m.get("relativePath"):
                model_file = os.path.join(base_dir, m["relativePath"])
                if os.path.exists(model_file):
                    actual_sha = compute_sha256(model_file)
                    if actual_sha != m["sha256"].lower():
                        return False, f"Security hash mismatch for {m['id']}: expected {m['sha256']}, got {actual_sha}", []

            validated.append(m)

        return True, "Manifest valid", validated
    except Exception as e:
        return False, f"Manifest validation error: {str(e)}", []
