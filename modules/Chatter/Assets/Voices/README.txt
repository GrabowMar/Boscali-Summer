Battlefield voice library

The authoring scripts referenced as `tools/` below live in nomodkit under `mods/boscalisummer/tools/`. Run them from the Boscali Summer checkout.

The 2026-11-17 ensemble replaces every former shipped chatter recording. Nine roles, 90 exact scripts, four acting takes per script: 360 speech clips. No runtime API or network generation.

`production.json` owns the ordered scripts, acting direction, event priority, voice identity and cast. `voices.tsv` is the compact transcript index. `generations.json` records the ElevenLabs model/generation IDs. The production flow is https://elevenlabs.io/app/flows/sN7Ssaxaz6p4JaCeQH2P .

Each `boscali-<role>/` folder contains `pack.txt` routing metadata and `<line-hash>-take-1..4.mp3`. The FNV line hash covers event, speaker selector and exact subtitle text. `zz-battlefield.chatter` replaces covered text blocks with these recorded scripts; variable context belongs beside the subtitle, never substituted into a mismatched recording.

Build with `tools/Build-BattlefieldVoices.py`; download raw takes to `.nomodkit/battlefield-voices/raw/<line-hash>-<take>.mp3` first. Use `--merge --line <new-id>` (repeat `--line` as needed) with the existing `--report` to add scripts without reencoding prior takes. Two-pass loudness processing targets -18 LUFS, with -2 dBTP processing headroom, mono 22050 Hz / 64 kbps, one restrained speech band. Verification checks the entire bank, scripts, hashes, measurements and routing metadata. Measurements are under `reports/battlefield-radio-2026-11-17/`.

The role folders are command, ground, captain, paratrooper, skywatch, tower, ace, wing and enemy. Captain evacuation calls are raised only when a friendly carrier is disabled. Ground calls follow actual ownership changes; paratroopers follow friendly insertion landings. Command superevent scripts describe real event catalog effects, without implying an actual ceasefire or invented movements.

Contextual microphone backgrounds are authored in `production.json` under `backgrounds`: combat for base capture/loss/withdrawal, naval emergency ambience for friendly ship loss/carrier evacuation, and wind/equipment/distant combat for insertions. Four generated beds rotate with the four acting takes. The offline builder bakes them beneath the words at -20 dB RMS for combat and -22 dB RMS for naval/insertion, with a 2500 Hz low-pass and 100/250 ms edge fades, then performs the normal loudness/peak acceptance. There are no extra runtime sources, loops or unit scans for these backgrounds. Only the 64 matching takes change; the other 296 voices, including post-nuclear survivors, retain their encoded bytes and measurements. Per-clip background IDs, source hashes, gains and processing recipes are recorded in the measurement report. Rebuild selected existing lines with `--merge --line <id>`; `--verify` rejects missing or stale background metadata.

`tools/Check-VoiceBackgrounds.py` checks the bake with a short looping synthetic bed, measures the encoded background-to-voice spectrum independently, and verifies that incremental mixing preserves unselected files and dry masters. `reports/battlefield-radio-2026-11-17/background-validation.json` records the complete-bank preservation check.

Recorded key-up and squelch effects are in `Core/Fx/Sfx/radio-keyup-0..3.wav` and `radio-tail-0..3.wav`, generated with ElevenLabs `eleven_text_to_sound_v2`; `tools/Build-BattlefieldEffects.py` prepares quiet, short PCM assets. They rotate at runtime and follow military receive/volume.

The SA-74N aftermath adds twelve fearful survivor scripts across ground, AWACS, pilot, naval and paratrooper roles. A mission-long communications blackout replaces station/deck programming with `Core/Fx/Sfx/nuclear-radio-static.wav`, a seamless quiet loop composed from four ElevenLabs generations. Routine military traffic stops; scattered survivor calls begin after 18-30 seconds and recur 40-75 seconds apart. Receiver settings still control what is heard. Mission reset clears the blackout and survivor sequence.
