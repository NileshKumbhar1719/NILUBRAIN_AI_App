import sys
import time
import os
import re
import whisper
import pytesseract
from PIL import Image

# Tesseract-OCR path
pytesseract.pytesseract.tesseract_cmd = r'C:\Program Files\Tesseract-OCR\tesseract.exe'

# UTF-8 एन्कोडिंग सेट करणे
sys.stdout.reconfigure(encoding="utf-8")
sys.stdin.reconfigure(encoding="utf-8")

# Whisper मॉडेल लोड करणे
print("Loading Whisper Model...", flush=True)
try:
    model = whisper.load_model("base")
except Exception:
    pass

print("AI_READY", flush=True)

def transcribe_audio(wav_path):
    try:
        wav_path = wav_path.strip('"').strip("'")
        result = model.transcribe(
            wav_path,
            language="en",
            beam_size=5,
            best_of=5,
            temperature=0.0,
            condition_on_previous_text=False,
            without_timestamps=True,
        )
        text = result["text"].strip()
        return text if text else "No speech detected"
    except Exception as e:
        return f"Error: {str(e)}"

def extract_text_from_image(img_path):
    try:
        img_path = img_path.strip('"').strip("'")
        if not os.path.exists(img_path):
            return "ERROR_NO_TEXT_FOUND"
            
        img = Image.open(img_path)
        custom_config = r'--oem 3 --psm 3'
        extracted_text = pytesseract.image_to_string(img, config=custom_config)
        
        cleaned = extracted_text.strip()
        if cleaned:
            return cleaned
        return "ERROR_NO_TEXT_FOUND"
    except Exception as e:
        return f"Error: {str(e)}"

# C# सोबत कम्युनिकेट करण्यासाठी सतत चालणारा लूप
while True:
    try:
        line = sys.stdin.readline()
        if not line:
            break
        line = line.strip()

        if line.startswith("TRANSCRIBE:"):
            wav_path = line[11:].strip()
            recognized_text = transcribe_audio(wav_path)
            print(f"RESULT:{recognized_text}", flush=True)
            
        elif line.startswith("OCR:"):
            img_path = line[4:].strip()
            screen_text = extract_text_from_image(img_path)
            print(f"RESULT:{screen_text}", flush=True)
            
    except Exception as e:
        print(f"RESULT:Error {str(e)}", flush=True)