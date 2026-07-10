import os
import urllib.request
from urllib.error import HTTPError, URLError

import mido
from mido import MidiFile, MidiTrack, Message


# ---------------------------------------------------------------------------
# Mussorgsky - Night on Bald Mountain
# MIDI loop extractor / arranger for game background music.
#
# Modes:
#
#   ARRANGEMENT_MODE = "original"
#       Keeps the original MIDI instruments.
#
#   ARRANGEMENT_MODE = "electric_guitar_main"
#       Keeps the original notes/content, but remaps likely high/melodic tracks
#       to electric/distorted guitar.
#       Lower strings, basses, timpani, brass, etc. are kept as-is.
#
# Important:
# - No new melody is composed here.
# - No drums are added.
# - No rock rhythm is added.
# - The script only cuts the MIDI and optionally changes selected instruments.
# ---------------------------------------------------------------------------

ARRANGEMENT_MODE = "electric_guitar_main"
# ARRANGEMENT_MODE = "original"

# Candidate durations around 3 minutes.
TARGET_SECONDS_LIST = [
    150.0,
    165.0,
    180.0,
    195.0,
    210.0,
]

# Night on Bald Mountain is commonly notated in 4/4/alla breve-like sections.
# For loop cutting, using 4 beats per bar works well for most MIDI reductions.
BEATS_PER_BAR = 4

SOURCE_MIDI = "mussorgsky_night_on_bald_mountain_original.mid"

# Kunst der Fuge exposes downloadable MIDI files, but the exact file URL may
# change. The script tries several likely direct URLs first.
SOURCE_URL_CANDIDATES = [
    # Common Kunst der Fuge layout candidates.
    "https://www.kunstderfuge.com/mussorgsky/night_on_bald_mountain.mid",
    "https://www.kunstderfuge.com/mussorgsky/night_on_a_bald_mountain.mid",
    "https://www.kunstderfuge.com/mussorgsky/mussorgsky_night_on_bald_mountain.mid",
    "https://www.kunstderfuge.com/mussorgsky/mussorgsky_night_on_a_bald_mountain.mid",

    # VGMusic mirror/sequence candidate from public search result.
    # Use this as fallback if Kunst der Fuge blocks direct download.
    "https://www.vgmusic.com/new-files/NightOnBaldMountain.mid",
    "http://www.vgmusic.com/new-files/NightOnBaldMountain.mid",
]

# General MIDI programs, zero-based:
# 27 = Clean Electric Guitar
# 29 = Overdriven Guitar
# 30 = Distortion Guitar
ELECTRIC_GUITAR_PROGRAM = 30

# Heuristic for choosing likely main/high melodic tracks.
MELODY_AVG_NOTE_THRESHOLD = 58
MELODY_MAX_NOTE_THRESHOLD = 72
MAX_GUITAR_TRACKS = 4


def make_request(url):
    return urllib.request.Request(
        url,
        headers={
            "User-Agent": (
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
                "AppleWebKit/537.36 (KHTML, like Gecko) "
                "Chrome/125.0 Safari/537.36"
            ),
            "Accept": "audio/midi,audio/*,*/*",
        },
    )


def looks_like_midi(data):
    return len(data) > 4 and data[:4] == b"MThd"


def download_source():
    if os.path.exists(SOURCE_MIDI):
        print(f"Using existing source MIDI: {SOURCE_MIDI}")
        return

    print("Downloading source MIDI...")

    last_error = None

    for url in SOURCE_URL_CANDIDATES:
        print(f"Trying: {url}")

        try:
            request = make_request(url)

            with urllib.request.urlopen(request, timeout=30) as response:
                data = response.read()

            if not looks_like_midi(data):
                print("  Skipped: response is not a MIDI file.")
                continue

            with open(SOURCE_MIDI, "wb") as f:
                f.write(data)

            print(f"Downloaded: {SOURCE_MIDI}")
            print(f"Source URL: {url}")
            return

        except HTTPError as e:
            last_error = e
            print(f"  HTTP error: {e.code}")

        except URLError as e:
            last_error = e
            print(f"  URL error: {e}")

        except Exception as e:
            last_error = e
            print(f"  Error: {e}")

    print()
    print("Could not download a MIDI file automatically.")
    print()
    print("Manual fallback:")
    print("  1. Open this page in your browser:")
    print("     https://www.kunstderfuge.com/mussorgsky.htm")
    print("  2. Download the MIDI for 'Night on a bald mountain'.")
    print(f"  3. Save it in this folder as: {SOURCE_MIDI}")
    print("  4. Run this script again.")
    print()

    if last_error:
        raise RuntimeError(f"Download failed. Last error: {last_error}")

    raise RuntimeError("Download failed.")


def get_tempo_events(mid):
    tempo_events = []

    for track in mid.tracks:
        abs_tick = 0

        for msg in track:
            abs_tick += msg.time

            if msg.type == "set_tempo":
                tempo_events.append((abs_tick, msg.tempo))

    tempo_events.sort(key=lambda x: x[0])

    if not tempo_events or tempo_events[0][0] != 0:
        tempo_events.insert(0, (0, 500000))  # MIDI default: 120 BPM

    compact = []

    for tick, tempo in tempo_events:
        if compact and compact[-1][0] == tick:
            compact[-1] = (tick, tempo)
        else:
            compact.append((tick, tempo))

    return compact


def seconds_to_tick(mid, seconds):
    tempo_events = get_tempo_events(mid)
    ticks_per_beat = mid.ticks_per_beat

    elapsed_seconds = 0.0

    for i, (tick, tempo) in enumerate(tempo_events):
        next_tick = tempo_events[i + 1][0] if i + 1 < len(tempo_events) else None

        if next_tick is None:
            remaining_seconds = seconds - elapsed_seconds
            return int(tick + mido.second2tick(remaining_seconds, ticks_per_beat, tempo))

        segment_ticks = next_tick - tick
        segment_seconds = mido.tick2second(segment_ticks, ticks_per_beat, tempo)

        if elapsed_seconds + segment_seconds >= seconds:
            remaining_seconds = seconds - elapsed_seconds
            return int(tick + mido.second2tick(remaining_seconds, ticks_per_beat, tempo))

        elapsed_seconds += segment_seconds

    return 0


def tick_to_seconds(mid, target_tick):
    tempo_events = get_tempo_events(mid)
    ticks_per_beat = mid.ticks_per_beat

    elapsed_seconds = 0.0

    for i, (tick, tempo) in enumerate(tempo_events):
        next_tick = tempo_events[i + 1][0] if i + 1 < len(tempo_events) else target_tick

        if target_tick <= tick:
            return elapsed_seconds

        segment_end_tick = min(target_tick, next_tick)
        segment_ticks = segment_end_tick - tick

        elapsed_seconds += mido.tick2second(segment_ticks, ticks_per_beat, tempo)

        if target_tick <= next_tick:
            return elapsed_seconds

    return elapsed_seconds


def round_down_to_bar(mid, tick):
    ticks_per_bar = mid.ticks_per_beat * BEATS_PER_BAR
    return (tick // ticks_per_bar) * ticks_per_bar


def is_note_on(msg):
    return msg.type == "note_on" and msg.velocity > 0


def is_note_off(msg):
    return msg.type == "note_off" or (msg.type == "note_on" and msg.velocity == 0)


def analyze_track(track):
    notes = []
    note_count = 0
    program_numbers = set()
    channels = set()

    for msg in track:
        if msg.type == "program_change":
            program_numbers.add(msg.program)
            channels.add(msg.channel)

        if hasattr(msg, "channel"):
            channels.add(msg.channel)

        if is_note_on(msg):
            notes.append(msg.note)
            note_count += 1

    if not notes:
        return {
            "note_count": 0,
            "avg_note": 0,
            "max_note": 0,
            "min_note": 0,
            "programs": program_numbers,
            "channels": channels,
        }

    return {
        "note_count": note_count,
        "avg_note": sum(notes) / len(notes),
        "max_note": max(notes),
        "min_note": min(notes),
        "programs": program_numbers,
        "channels": channels,
    }


def select_guitar_tracks(source_mid):
    candidates = []

    for index, track in enumerate(source_mid.tracks):
        stats = analyze_track(track)

        if stats["note_count"] == 0:
            continue

        if 9 in stats["channels"]:
            continue

        looks_melodic = (
            stats["avg_note"] >= MELODY_AVG_NOTE_THRESHOLD
            or stats["max_note"] >= MELODY_MAX_NOTE_THRESHOLD
        )

        if not looks_melodic:
            continue

        score = (
            stats["note_count"] * 1.0
            + stats["avg_note"] * 4.0
            + stats["max_note"] * 2.0
        )

        candidates.append((score, index, stats))

    candidates.sort(reverse=True)

    selected = [index for _, index, _ in candidates[:MAX_GUITAR_TRACKS]]

    print()
    print("Track analysis:")
    for score, index, stats in candidates[:12]:
        marker = " <-- guitar" if index in selected else ""
        print(
            f"  track {index:02d}: "
            f"notes={stats['note_count']:5d}, "
            f"avg={stats['avg_note']:5.1f}, "
            f"min={stats['min_note']:3d}, "
            f"max={stats['max_note']:3d}, "
            f"programs={sorted(stats['programs'])}, "
            f"channels={sorted(stats['channels'])}"
            f"{marker}"
        )

    print()

    return set(selected)


def transform_message_for_arrangement(msg, track_index, guitar_track_indexes):
    copied = msg.copy()

    if ARRANGEMENT_MODE != "electric_guitar_main":
        return copied

    if track_index not in guitar_track_indexes:
        return copied

    if copied.type == "program_change":
        copied.program = ELECTRIC_GUITAR_PROGRAM

    if copied.type == "note_on" and copied.velocity > 0:
        copied.velocity = min(127, int(copied.velocity * 1.12) + 5)

    return copied


def ensure_guitar_program_at_start(new_track, source_track, track_index, guitar_track_indexes):
    if ARRANGEMENT_MODE != "electric_guitar_main":
        return

    if track_index not in guitar_track_indexes:
        return

    channel = None

    for msg in source_track:
        if hasattr(msg, "channel") and msg.channel != 9:
            channel = msg.channel
            break

    if channel is None:
        channel = 0

    new_track.append(
        Message(
            "program_change",
            program=ELECTRIC_GUITAR_PROGRAM,
            channel=channel,
            time=0,
        )
    )


def copy_until_tick(source_mid, cut_tick, guitar_track_indexes):
    output_mid = MidiFile(
        ticks_per_beat=source_mid.ticks_per_beat,
        type=source_mid.type,
        charset=source_mid.charset,
        clip=source_mid.clip,
        debug=source_mid.debug,
    )

    for track_index, track in enumerate(source_mid.tracks):
        new_track = MidiTrack()
        output_mid.tracks.append(new_track)

        ensure_guitar_program_at_start(new_track, track, track_index, guitar_track_indexes)

        abs_tick = 0
        last_written_tick = 0
        active_notes = set()

        for msg in track:
            abs_tick += msg.time

            if abs_tick > cut_tick:
                break

            copied_msg = transform_message_for_arrangement(
                msg,
                track_index,
                guitar_track_indexes,
            )

            copied_msg.time = abs_tick - last_written_tick

            new_track.append(copied_msg)
            last_written_tick = abs_tick

            if hasattr(msg, "channel") and hasattr(msg, "note"):
                key = (msg.channel, msg.note)

                if is_note_on(msg):
                    active_notes.add(key)
                elif is_note_off(msg):
                    active_notes.discard(key)

        first = True

        for channel, note in sorted(active_notes):
            delay = cut_tick - last_written_tick if first else 0

            new_track.append(
                Message(
                    "note_off",
                    note=note,
                    velocity=0,
                    channel=channel,
                    time=delay,
                )
            )

            last_written_tick = cut_tick
            first = False

        if last_written_tick < cut_tick:
            new_track.append(
                Message(
                    "control_change",
                    control=64,
                    value=0,
                    channel=0,
                    time=cut_tick - last_written_tick,
                )
            )

    return output_mid


def make_output_name(actual_seconds):
    rounded = int(round(actual_seconds))

    if ARRANGEMENT_MODE == "original":
        suffix = "original"
    elif ARRANGEMENT_MODE == "electric_guitar_main":
        suffix = "guitar_main"
    else:
        suffix = ARRANGEMENT_MODE

    return f"mussorgsky_bald_mountain_{suffix}_loop_{rounded}s.mid"


def main():
    download_source()

    source_mid = MidiFile(SOURCE_MIDI)

    print()
    print(f"Source MIDI: {SOURCE_MIDI}")
    print(f"Ticks per beat: {source_mid.ticks_per_beat}")
    print(f"Tracks: {len(source_mid.tracks)}")
    print(f"Arrangement mode: {ARRANGEMENT_MODE}")

    if ARRANGEMENT_MODE == "electric_guitar_main":
        guitar_track_indexes = select_guitar_tracks(source_mid)
    else:
        guitar_track_indexes = set()

    for target_seconds in TARGET_SECONDS_LIST:
        target_tick = seconds_to_tick(source_mid, target_seconds)
        cut_tick = round_down_to_bar(source_mid, target_tick)
        actual_seconds = tick_to_seconds(source_mid, cut_tick)

        output_name = make_output_name(actual_seconds)

        print("----------------------------------------")
        print(f"Requested duration: {target_seconds:.1f} seconds")
        print(f"Rounded duration:   {actual_seconds:.2f} seconds")
        print(f"Cut tick:           {cut_tick}")
        print(f"Output:             {output_name}")

        output_mid = copy_until_tick(
            source_mid,
            cut_tick,
            guitar_track_indexes,
        )

        output_mid.save(output_name)

    print("----------------------------------------")
    print("Done.")
    print()
    print("Generated MIDI candidates.")
    print()
    if ARRANGEMENT_MODE == "original":
        print("Mode: original. No instruments changed.")
    else:
        print("Mode: electric_guitar_main.")
        print("Likely high/melodic tracks were remapped to electric/distorted guitar.")
        print("Other tracks were preserved.")


if __name__ == "__main__":
    main()