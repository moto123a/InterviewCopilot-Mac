# -*- coding: utf-8 -*-
import os
import sys
import time
import queue
import threading
import argparse
import numpy as np
import sounddevice as sd
from faster_whisper import WhisperModel

parser = argparse.ArgumentParser()
parser.add_argument("--device_index", type=int, default=None)
parser.add_argument("--list_devices", action="store_true")
args = parser.parse_args()

if args.list_devices:
    try:
        devs = sd.query_devices()
        with open("devices.txt", "w", encoding="utf-8") as f:
            for i, dev in enumerate(devs):
                if dev['max_input_channels'] > 0:
                    name = dev['name']
                    if "Stereo Mix" in name or "Loopback" in name: 
                        name = f"[SYSTEM] {name}"
                    f.write(f"{i}|{name}\n")
    except: pass
    sys.exit(0)

print("Loading Whisper Model...")
sys.stdout.flush()

try:
    model = WhisperModel("base.en", device="cuda", compute_type="float16")
    print("Model Loaded (GPU).")
except:
    print("GPU not found. Using CPU.")
    model = WhisperModel("base.en", device="cpu", compute_type="int8")

sys.stdout.flush()

samplerate = 16000
audio_q = queue.Queue()
audio_buffer = np.zeros(0, dtype=np.float32)
conversation_history = "" 
mic_paused = False

def is_paused(): return os.path.exists("pause.flag")

def audio_callback(indata, frames, time_info, status):
    if mic_paused or is_paused(): 
        try:
            with open("volume.txt", "w") as f: f.write("0")
        except: pass
        return

    rms = np.sqrt(np.mean(indata**2))
    vol_int = int(rms * 800)
    if vol_int > 100: vol_int = 100
    
    try:
        with open("volume.txt", "w") as f: f.write(str(vol_int))
    except: pass

    audio_q.put(indata.copy())

def state_watcher():
    global mic_paused, conversation_history
    last_pause = False

    while True:
        if os.path.exists("clear.flag"):
            conversation_history = "" 
            mic_paused = False
            with open("latest.txt", "w", encoding="utf-8") as f: f.write("")
            try: os.remove("clear.flag")
            except: pass
            print("\n--- CLEARED ---\n")
            sys.stdout.flush()

        paused = is_paused()
        if paused and not last_pause:
            mic_paused = True
        elif not paused and last_pause:
            mic_paused = False
            
        last_pause = paused
        time.sleep(0.1)

def transcriber():
    global audio_buffer, conversation_history
    print("Transcriber Started.")
    CHUNK_THRESHOLD = int(samplerate * 1.5)
    
    while True:
        if mic_paused:
            time.sleep(0.1)
            continue
            
        try:
            data = audio_q.get(timeout=1)
            mono = data[:, 0] if data.ndim > 1 else data
            audio_buffer = np.concatenate([audio_buffer, mono])
            
            if audio_buffer.shape[0] >= CHUNK_THRESHOLD:
                chunk = audio_buffer.copy()
                audio_buffer = np.zeros(0, dtype=np.float32)
                
                segments, _ = model.transcribe(chunk, language="en", vad_filter=True)
                new_text = " ".join([s.text for s in segments]).strip()
                
                if new_text:
                    conversation_history = (conversation_history + " " + new_text).strip()
                    with open("latest.txt", "w", encoding="utf-8") as f:
                        f.write(conversation_history)
                    print(f"Recognized: {new_text}")
                    sys.stdout.flush()
        except: continue

threading.Thread(target=transcriber, daemon=True).start()
threading.Thread(target=state_watcher, daemon=True).start()

try:
    with sd.InputStream(samplerate=samplerate, blocksize=4000, device=args.device_index, channels=1, callback=audio_callback):
        while True: sd.sleep(100)
except Exception as e:
    print(f"Mic Error: {e}")