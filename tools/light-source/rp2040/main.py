# MicroPython firmware: RP2040-Zero + BH1750, four-wire verification setup.
# RED VCC -> 3V3; BLACK GND -> GND; WHITE DAT -> GP0; BROWN SCL -> GP1.
# YELLOW ADDR currently disconnected on your tested module.
# For a permanent four-core cable, join YELLOW to BLACK at the sensor end.
# GP2 is unused. This firmware is NOT for CH552 or ESP32-C3.
from machine import Pin, I2C, unique_id
import sys
import select
import time
import json
import binascii

source_id = "rp2040-bh1750-" + binascii.hexlify(unique_id()).decode()
i2c = I2C(0, sda=Pin(0), scl=Pin(1), freq=100000)
address = None
history = []  # RAM only: [sequence, ticks_ms, lux]; oldest entries are overwritten.
sequence = 0
HISTORY_CAPACITY = 64
poller = select.poll()
poller.register(sys.stdin, select.POLLIN)

def measure():
    global address
    if address is None:
        found = i2c.scan()
        address = next((a for a in (0x23, 0x5C) if a in found), None)
        if address is None:
            raise OSError("BH1750 not found")
    i2c.writeto(address, b"\x01")
    i2c.writeto(address, b"\x20")  # One-time high-resolution measurement.
    # Wait for a fresh conversion after the host request.
    time.sleep_ms(200)
    data = i2c.readfrom(address, 2)
    return round(((data[0] << 8) | data[1]) / 1.2, 2)

while True:
    if not poller.poll(100):
        continue
    line = sys.stdin.readline().strip()
    parts = line.split()
    if len(parts) != 2 or parts[0] not in ("READ", "PING", "HISTORY"):
        continue
    command, request_id = parts
    if len(request_id) != 32:
        continue
    if command == "PING":
        print(json.dumps({"requestId": request_id, "sourceId": source_id,
                          "protocol": "fc-light-usb/1"}))
        continue
    if command == "HISTORY":
        print(json.dumps({"requestId": request_id, "sourceId": source_id,
                          "timebase": "ticks_ms", "capacity": HISTORY_CAPACITY,
                          "samples": history}))
        continue
    result = {"requestId": request_id, "sourceId": source_id,
              "valid": False, "lux": None}
    try:
        result["lux"] = measure()
        result["valid"] = True
        sequence += 1
        history.append([sequence, time.ticks_ms(), result["lux"]])
        if len(history) > HISTORY_CAPACITY:
            history.pop(0)
    except OSError:
        address = None
    print(json.dumps(result))
