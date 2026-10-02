#!/usr/bin/env python3
"""One-off smoke test of the Phobia APK on an emulator.

Installs the released APK (ARM, run through the emulator's ARM translation) and an x86_64 build of the
same commit, starts each, walks the first run - terms, create a wallet, the recovery phrase, the backup
check - then the home screen and the main pages, and keeps a screenshot, the UI tree and the log of
every step in OUT. Nothing here signs or sends anything.
"""
import json
import os
import re
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

OUT = sys.argv[1] if len(sys.argv) > 1 else "smoke"
PKG = "com.thefear.phobia"
PASSWORD = "PhobiaSmokeTest2026"
# The BIP39 reference test vector: public, valid, never used for money.
TEST_PHRASE = "legal winner thank year wave sausage worth useful legal winner thank yellow"

os.makedirs(OUT, exist_ok=True)
LOG = open(os.path.join(OUT, "steps.txt"), "a", encoding="utf-8")
RESULT = {}
TAG = "x"
COUNTER = [0]


def say(msg):
    print(msg, flush=True)
    LOG.write(msg + "\n")
    LOG.flush()


def adb(*args, timeout=180):
    try:
        return subprocess.run(["adb", *args], capture_output=True, timeout=timeout)
    except subprocess.TimeoutExpired:
        say(f"adb {' '.join(args)}: timed out")
        return subprocess.CompletedProcess(args, 1, b"", b"timeout")


def sh(cmd, timeout=120):
    return adb("shell", cmd, timeout=timeout).stdout.decode(errors="replace")


def shot(name):
    COUNTER[0] += 1
    data = adb("exec-out", "screencap", "-p").stdout
    path = os.path.join(OUT, f"{TAG}-{COUNTER[0]:02d}-{name}.png")
    with open(path, "wb") as f:
        f.write(data)
    with open(path.replace(".png", ".xml"), "w", encoding="utf-8") as f:
        f.write(dump())
    say(f"  shot {os.path.basename(path)}")


def dump():
    adb("shell", "uiautomator", "dump", "/sdcard/ui.xml", timeout=60)
    return adb("shell", "cat", "/sdcard/ui.xml").stdout.decode(errors="replace")


def nodes(xml):
    start = xml.find("<?xml")
    if start < 0:
        start = xml.find("<hierarchy")
    if start < 0:
        return []
    try:
        root = ET.fromstring(xml[start:])
    except ET.ParseError:
        return []
    found = []
    for el in root.iter("node"):
        b = [int(v) for v in re.findall(r"-?\d+", el.get("bounds", ""))]
        if len(b) == 4:
            found.append((el.get("text", ""), el.get("content-desc", ""), el.get("class", ""), tuple(b)))
    return found


def find(pattern, xml=None):
    rx = re.compile(pattern, re.I)
    for text, desc, _cls, (x1, y1, x2, y2) in nodes(xml if xml is not None else dump()):
        if (rx.search(text) or rx.search(desc)) and x2 > x1 and y2 > y1:
            return (x1, y1, x2, y2)
    return None


def wait_for(pattern, seconds=30):
    end = time.time() + seconds
    while time.time() < end:
        b = find(pattern)
        if b:
            return b
        time.sleep(1.5)
    say(f"  not seen within {seconds}s: /{pattern}/")
    return None


def tap_xy(x, y, pause=1.5):
    adb("shell", "input", "tap", str(int(x)), str(int(y)))
    time.sleep(pause)


def tap(pattern, seconds=20, pause=1.5):
    b = wait_for(pattern, seconds)
    if not b:
        return False
    x1, y1, x2, y2 = b
    say(f"  tap /{pattern}/ at {(x1 + x2) // 2},{(y1 + y2) // 2}")
    tap_xy((x1 + x2) / 2, (y1 + y2) / 2, pause)
    return True


def tap_below(pattern, dp=34, seconds=20):
    """Tap the field under a label (a TextBox sits right below its caption)."""
    b = wait_for(pattern, seconds)
    if not b:
        return False
    x1, y1, x2, y2 = b
    y = y2 + dp * DENSITY
    say(f"  tap below /{pattern}/ at {(x1 + x2) // 2},{int(y)}")
    tap_xy((x1 + x2) / 2, y)
    return True


def type_text(text):
    adb("shell", "input", "text", text.replace(" ", "%s"))
    time.sleep(1)


def swipe_up(fraction=0.45):
    x = WIDTH // 2
    adb("shell", "input", "swipe", str(x), str(int(HEIGHT * 0.75)), str(x), str(int(HEIGHT * (0.75 - fraction))), "400")
    time.sleep(1.2)


def swipe_down():
    x = WIDTH // 2
    adb("shell", "input", "swipe", str(x), str(int(HEIGHT * 0.3)), str(x), str(int(HEIGHT * 0.85)), "300")
    time.sleep(1)


def alive():
    return sh(f"pidof {PKG}").strip()


def launch():
    act = sh(f"cmd package resolve-activity --brief {PKG}").strip().splitlines()
    act = act[-1].strip() if act else ""
    say(f"  activity {act}")
    out = sh(f"am start -W -n {act}", timeout=180)
    total = re.search(r"TotalTime:\s*(\d+)", out)
    say("  " + " ".join(out.split()))
    return int(total.group(1)) if total else None


def save_log(name):
    log = adb("logcat", "-d", "-v", "time", timeout=120).stdout.decode(errors="replace")
    with open(os.path.join(OUT, f"{TAG}-logcat-{name}.txt"), "w", encoding="utf-8") as f:
        f.write(log)
    bad = [l for l in log.splitlines()
           if re.search(r"FATAL EXCEPTION|AndroidRuntime: |Unhandled ?Exception|monodroid.*(error|Error)|"
                        r"System\.\w*Exception|Avalonia.*(error|Error)|ANR in", l)]
    with open(os.path.join(OUT, f"{TAG}-problems-{name}.txt"), "w", encoding="utf-8") as f:
        f.write("\n".join(bad))
    return bad


def first_run(route):
    """Terms, then create (with the backup check) or import. Returns True on reaching home."""
    if wait_for(r"18 or older", 60):
        shot("terms")
        tap(r"18 or older")
        tap(r"read and accept the Terms")
        shot("terms-ticked")
        tap(r"I understand")
    if not wait_for(r"Create new wallet", 30):
        shot("no-welcome")
        return False
    shot("welcome")

    if route == "import":
        tap(r"Import existing wallet")
        shot("import")
        tap_below(r"VAULT PASSWORD") or tap(r"At least 12")
        type_text(PASSWORD)
        tap_below(r"CONFIRM PASSWORD") or tap(r"Repeat password")
        type_text(PASSWORD)
        tap_below(r"RECOVERY PHRASE") or tap(r"word1 word2")
        type_text(TEST_PHRASE)
        shot("import-filled")
        if not tap(r"Import & encrypt", 10):
            swipe_up()
            tap(r"Import & encrypt", 10)
        return wait_for(r"TOTAL BALANCE|Portfolio|My assets", 120) is not None

    tap(r"Create new wallet")
    shot("create")
    tap_below(r"VAULT PASSWORD") or tap(r"At least 12")
    type_text(PASSWORD)
    tap_below(r"CONFIRM PASSWORD") or tap(r"Repeat password")
    type_text(PASSWORD)
    shot("create-filled")
    if not tap(r"Create local wallet", 10):
        swipe_up()
        tap(r"Create local wallet", 10)
    if not wait_for(r"Write down your recovery phrase", 120):
        shot("no-backup-screen")
        return False
    # FLAG_SECURE: this screenshot is expected to come out black.
    shot("backup-secure")
    phrase = None
    for text, desc, _c, _b in nodes(dump()):
        words = (text or desc).split()
        if len(words) in (12, 24) and all(re.fullmatch(r"[a-z]+", w) for w in words):
            phrase = words
    RESULT["phrase_readable_by_accessibility"] = phrase is not None
    if not phrase:
        say("  the phrase is not in the UI tree - cannot pass the backup check")
        return False
    if not tap(r"written it down", 10):
        swipe_up()
        tap(r"written it down", 10)
    wait_for(r"Confirm your backup", 20)
    shot("verify")
    xml = dump()
    labels = [(t or d, b) for t, d, _c, b in nodes(xml) if re.fullmatch(r"(?i)word\s+(\d+)", (t or d).strip())]
    say(f"  verify labels: {[l for l, _ in labels]}")
    for label, (x1, y1, x2, y2) in labels:
        n = int(re.search(r"\d+", label).group())
        tap_xy((x1 + x2) / 2, y2 + 30 * DENSITY)
        type_text(phrase[n - 1])
    shot("verify-filled")
    if not tap(r"Verify & finish", 10):
        swipe_up()
        tap(r"Verify & finish", 10)
    return wait_for(r"TOTAL BALANCE|Portfolio|My assets", 60) is not None


def tour():
    time.sleep(8)
    shot("home")
    swipe_up(0.5)
    shot("home-scrolled")
    swipe_up(0.5)
    shot("home-bottom")
    swipe_down(); swipe_down(); swipe_down()
    # The raised crystal: quick actions.
    if not tap(r"Quick actions", 5):
        tap_xy(WIDTH / 2, HEIGHT - 70 * DENSITY)
    shot("quick-actions")
    tap(r"^Receive$", 8)
    time.sleep(3)
    shot("receive")
    adb("shell", "input", "keyevent", "4")   # back: home
    time.sleep(2)
    shot("back-to-home")
    for name, rx in (("activity", r"^Activity$"), ("explore", r"^Explore$"), ("settings", r"^Settings$")):
        tap(rx, 8)
        time.sleep(3)
        shot(name)
        swipe_up(0.5)
        shot(name + "-scrolled")
    tap(r"^Home$", 8)
    time.sleep(2)
    tap(r"Main wallet|Wallet 1", 8)
    shot("wallet-sheet")
    adb("shell", "input", "keyevent", "4")
    time.sleep(1)
    tap(r"^Send$", 8)
    time.sleep(2)
    shot("send")
    adb("shell", "input", "keyevent", "4")
    time.sleep(1)
    tap(r"^Swap$", 8)
    time.sleep(2)
    shot("swap")
    adb("shell", "input", "keyevent", "4")
    time.sleep(1)
    shot("end")


def run_build(apk, tag, route, full):
    global TAG
    TAG = tag
    COUNTER[0] = 0
    say(f"== {tag}: {apk}")
    adb("uninstall", PKG)
    r = adb("install", "-r", apk, timeout=600)
    msg = (r.stdout + r.stderr).decode(errors="replace").strip()
    say("  install: " + msg.replace("\n", " | "))
    RESULT[f"{tag}_installed"] = "Success" in msg
    if "Success" not in msg:
        return
    adb("logcat", "-c")
    RESULT[f"{tag}_cold_start_ms"] = launch()
    time.sleep(12)
    RESULT[f"{tag}_alive_after_start"] = bool(alive())
    shot("start")
    if full:
        RESULT[f"{tag}_reached_home"] = first_run(route)
        if RESULT[f"{tag}_reached_home"]:
            tour()
    RESULT[f"{tag}_alive_at_end"] = bool(alive())
    RESULT[f"{tag}_problem_lines"] = len(save_log("all"))


def main():
    global DENSITY, WIDTH, HEIGHT, TAG
    size = re.search(r"(\d+)x(\d+)", sh("wm size"))
    WIDTH, HEIGHT = (int(size.group(1)), int(size.group(2))) if size else (1080, 2400)
    dens = re.search(r"(\d+)", sh("wm density"))
    DENSITY = (int(dens.group(1)) if dens else 420) / 160
    RESULT["device"] = {
        "model": sh("getprop ro.product.model").strip(),
        "android": sh("getprop ro.build.version.release").strip(),
        "abis": sh("getprop ro.product.cpu.abilist").strip(),
        "screen": f"{WIDTH}x{HEIGHT} @ {DENSITY:.2f}x",
    }
    say(json.dumps(RESULT["device"]))
    adb("shell", "settings", "put", "secure", "show_ime_with_hard_keyboard", "0")

    try:
        run_build("phobia-release.apk", "release", "create", full=False)
    except Exception as e:  # keep going: the second build is the thorough one
        say(f"release run failed: {e!r}")
    try:
        run_build("phobia-x64.apk", "x64", "create", full=True)
        if not RESULT.get("x64_reached_home"):
            # The backup check could not be driven: walk the import route instead.
            save_log("create")
            sh(f"pm clear {PKG}")
            adb("logcat", "-c")
            TAG = "x64-import"
            COUNTER[0] = 0
            launch()
            time.sleep(10)
            RESULT["x64_import_reached_home"] = first_run("import")
            if RESULT["x64_import_reached_home"]:
                tour()
            RESULT["x64_import_alive_at_end"] = bool(alive())
            RESULT["x64_import_problem_lines"] = len(save_log("import"))
    except Exception as e:
        say(f"x64 run failed: {e!r}")
        save_log("exception")

    with open(os.path.join(OUT, "result.json"), "w", encoding="utf-8") as f:
        json.dump(RESULT, f, indent=2)
    say(json.dumps(RESULT, indent=2))


DENSITY, WIDTH, HEIGHT = 2.625, 1080, 2400
if __name__ == "__main__":
    main()
