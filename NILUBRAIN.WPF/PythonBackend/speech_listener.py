
import sys
import os
import traceback
import shutil
from pathlib import Path

# --------------------------------------------------
# UTF-8 COMMUNICATION
# --------------------------------------------------

sys.stdout.reconfigure(
    encoding="utf-8",
    line_buffering=True
)

sys.stdin.reconfigure(encoding="utf-8")

# --------------------------------------------------
# PYTHON BACKEND DIRECTORY
# --------------------------------------------------

BASE_DIR = Path(__file__).resolve().parent

# Make bundled FFmpeg discoverable.
os.environ["PATH"] = (
    str(BASE_DIR)
    + os.pathsep
    + str(BASE_DIR / "DLLs")
    + os.pathsep
    + os.environ.get("PATH", "")
)

# --------------------------------------------------
# MSIX SAFE APP DATA
# --------------------------------------------------

LOCAL_APP_DATA = Path(
    os.environ.get(
        "LOCALAPPDATA",
        str(Path.home() / "AppData" / "Local")
    )
)

APP_DATA = LOCAL_APP_DATA / "NILUBRAIN AI"

MODEL_CACHE = APP_DATA / "WhisperModels"

TORCH_CACHE = APP_DATA / "TorchCache"

APP_DATA.mkdir(parents=True, exist_ok=True)
MODEL_CACHE.mkdir(parents=True, exist_ok=True)
TORCH_CACHE.mkdir(parents=True, exist_ok=True)

os.environ["TORCH_HOME"] = str(TORCH_CACHE)

# --------------------------------------------------
# LOGGING
# --------------------------------------------------

LOG_FILE = APP_DATA / "whisper_log.txt"


def log(message):

    message = str(message)

    try:
        with open(
            LOG_FILE,
            "a",
            encoding="utf-8"
        ) as f:

            f.write(message + "\n")

    except Exception:
        pass

    print(message, file=sys.stderr, flush=True)


# --------------------------------------------------
# GLOBAL MODEL
# --------------------------------------------------

model = None


# --------------------------------------------------
# LOAD WHISPER
# --------------------------------------------------

def load_whisper():

    global model

    log("Starting Whisper initialization...")

    log("Python executable: " + sys.executable)

    log("Python version: " + sys.version)

    log("Backend directory: " + str(BASE_DIR))

    log("Model directory: " + str(MODEL_CACHE))

    # Verify FFmpeg
    ffmpeg_path = shutil.which("ffmpeg")

    if not ffmpeg_path:

        raise RuntimeError(
            "FFmpeg not found. Package ffmpeg.exe "
            "inside PythonBackend."
        )

    log("FFmpeg found: " + ffmpeg_path)

    # Import dependencies
    import torch

    log("Torch imported: " + torch.__version__)

    import whisper

    log("Whisper imported successfully.")

    # CPU mode
    log("Loading Whisper base model...")

    model = whisper.load_model(
        "base",
        device="cpu",
        download_root=str(MODEL_CACHE)
    )

    log("Whisper model loaded successfully.")


# --------------------------------------------------
# TRANSCRIBE AUDIO
# --------------------------------------------------

def transcribe_audio(wav_path):

    try:

        if model is None:

            return "Error: Whisper model is not loaded"

        wav_path = wav_path.strip().strip('"').strip("'")

        if not os.path.isfile(wav_path):

            return "Error: WAV file not found"

        log("Transcription started: " + wav_path)

        result = model.transcribe(
            wav_path,
            language="en",
            fp16=False,
            beam_size=5,
            best_of=5,
            temperature=0,
            condition_on_previous_text=False,
            without_timestamps=True
        )

        text = result.get("text", "").strip()

        log("Transcription completed.")

        return text if text else "No speech detected"

    except Exception as ex:

        log(traceback.format_exc())

        return "Error: " + str(ex)


# --------------------------------------------------
# INITIALIZATION
# --------------------------------------------------

try:

    load_whisper()

    print("AI_READY", flush=True)

except Exception as ex:

    log("WHISPER INITIALIZATION FAILED")

    log(traceback.format_exc())

    print(
        "AI_ERROR:" + str(ex).replace("\n", " "),
        flush=True
    )


# --------------------------------------------------
# PERSISTENT COMMAND LISTENER
# --------------------------------------------------

for line in sys.stdin:

    try:

        line = line.strip()

        if not line:
            continue

        if line.startswith("TRANSCRIBE:"):

            wav_path = line[len("TRANSCRIBE:"):].strip()

            result = transcribe_audio(wav_path)

            result = result.replace("\r", " ")
            result = result.replace("\n", " ")

            print("RESULT:" + result, flush=True)

        elif line == "EXIT":

            log("Python backend exiting.")

            break

        else:

            print(
                "RESULT:Error: Unknown command",
                flush=True
            )

    except Exception as ex:

        log(traceback.format_exc())

        print(
            "RESULT:Error: " + str(ex),
            flush=True
        )
