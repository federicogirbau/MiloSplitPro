import argparse
import json
import os
import sys
import threading

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')
if hasattr(sys.stderr, 'reconfigure'):
    sys.stderr.reconfigure(encoding='utf-8')
if hasattr(sys.stdin, 'reconfigure'):
    sys.stdin.reconfigure(encoding='utf-8')

from stem_orchestrator import StemOrchestrator
from manifest_validator import validate_models_manifest

def print_event(event_dict: dict):
    """Prints JSON Lines event to stdout immediately with flush."""
    json_str = json.dumps(event_dict, ensure_ascii=False)
    sys.stdout.write(json_str + "\n")
    sys.stdout.flush()

def main():
    parser = argparse.ArgumentParser(description="Milo Split Pro Local AI Engine")
    parser.add_argument("--input", required=True, help="Input audio file path")
    parser.add_argument("--output-dir", required=True, help="Destination folder for separated stems")
    parser.add_argument("--stems", required=True, help="Comma-separated list of stems to separate")
    parser.add_argument("--device", default="auto", choices=["auto", "cuda", "cpu"], help="Hardware acceleration")
    parser.add_argument("--complement", action="store_true", default=True, help="Generate Other complement")
    parser.add_argument("--format", default="mp3", choices=["mp3", "wav", "flac", "ogg"], help="Output audio format")
    parser.add_argument("--manifest", default=None, help="Path to models.manifest.json for verification")

    args = parser.parse_args()

    # Security Manifest Verification (Fail-Closed)
    if args.manifest and os.path.exists(args.manifest):
        is_valid, msg, models = validate_models_manifest(args.manifest)
        if not is_valid:
            print_event({
                "stage": "Failed",
                "progress": 0.0,
                "message": "Fallo en la verificación de seguridad de modelos.",
                "errorDetails": msg
            })
            sys.exit(2)

    stems_list = [s.strip() for s in args.stems.split(",") if s.strip()]
    if not stems_list:
        print_event({
            "stage": "Failed",
            "progress": 0.0,
            "message": "No se especificó ninguna pista para separar.",
            "errorDetails": "La lista de stems está vacía."
        })
        sys.exit(1)

    orchestrator = StemOrchestrator(progress_callback=print_event)

    # Listen for cancellation on a background thread from stdin
    def listen_cancel():
        try:
            for line in sys.stdin:
                line_str = line.strip()
                if line_str == "CANCEL" or (line_str.startswith("{") and "cancel" in line_str.lower()):
                    orchestrator.is_cancelled = True
                    break
        except Exception:
            pass

    t = threading.Thread(target=listen_cancel, daemon=True)
    t.start()

    try:
        result = orchestrator.separate(
            input_file=args.input,
            output_dir=args.output_dir,
            selected_stems=stems_list,
            device=args.device,
            generate_complement=args.complement,
            output_format=args.format
        )

        if result.get("success"):
            print_event({
                "stage": "Completed",
                "progress": 100.0,
                "message": "Proceso finalizado.",
                "result": result
            })
            sys.exit(0)
        elif result.get("cancelled"):
            sys.exit(3)
        else:
            sys.exit(1)
    except Exception as ex:
        print_event({
            "stage": "Failed",
            "progress": 0.0,
            "message": f"Error durante la separación: {str(ex)}",
            "errorDetails": str(ex)
        })
        sys.stderr.write(f"Exception: {str(ex)}\n")
        sys.exit(1)

if __name__ == "__main__":
    main()
