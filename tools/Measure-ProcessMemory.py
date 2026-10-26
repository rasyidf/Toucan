#!/usr/bin/env python3
"""Sample Linux process-tree RSS for build and packaged-app memory checks."""
import argparse, json, os, signal, statistics, subprocess, time
from pathlib import Path
parser = argparse.ArgumentParser()
parser.add_argument('--output', required=True)
parser.add_argument('--log', required=True)
parser.add_argument('--duration', type=float, default=0)
parser.add_argument('--primary-name', default='')
parser.add_argument('command', nargs=argparse.REMAINDER)
args = parser.parse_args()
command = args.command[1:] if args.command[:1] == ['--'] else args.command
if not command:
    parser.error('provide a command after --')
if not Path('/proc/self/stat').is_file():
    parser.error('this sampler requires Linux /proc')
page = os.sysconf('SC_PAGE_SIZE')
start = time.monotonic()
samples = []
with open(args.log, 'w') as log:
    proc = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
    survived = False
    while True:
        snapshot = {}
        for path in Path('/proc').glob('[0-9]*'):
            try:
                raw = (path / 'stat').read_text()
                fields = raw.rsplit(')', 1)[1].split()
                snapshot[int(path.name)] = (int(fields[1]), int(fields[21]) * page, (path / 'comm').read_text().strip())
            except (OSError, ValueError, IndexError):
                pass
        descendants = {proc.pid}
        while True:
            added = {pid for pid, (parent, _, _) in snapshot.items() if parent in descendants}
            if added.issubset(descendants):
                break
            descendants |= added
        tree = sum(snapshot[pid][1] for pid in descendants if pid in snapshot)
        primary = sum(snapshot[pid][1] for pid in descendants if pid in snapshot and snapshot[pid][2] == args.primary_name)
        elapsed = time.monotonic() - start
        samples.append((elapsed, tree, primary))
        if proc.poll() is not None:
            break
        if args.duration and elapsed >= args.duration:
            survived = True
            os.killpg(proc.pid, signal.SIGTERM)
            try:
                proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                os.killpg(proc.pid, signal.SIGKILL)
                proc.wait()
            break
        time.sleep(.1)
result = {
    'command': command, 'elapsedSeconds': round(time.monotonic() - start, 2),
    'sampleIntervalSeconds': .1, 'exitCode': proc.returncode,
    'survivedObservation': survived, 'peakProcessTreeRssBytes': max(s[1] for s in samples),
    'primaryProcessName': args.primary_name, 'peakPrimaryRssBytes': max(s[2] for s in samples),
    'settledPrimaryRssBytes': statistics.median(s[2] for s in samples if s[0] >= max(0, samples[-1][0] - 5)),
    'note': 'Process-tree RSS can double-count shared pages; primary RSS excludes the virtual display server.'
}
Path(args.output).write_text(json.dumps(result, indent=2) + '\n')
print(json.dumps(result))
raise SystemExit(0 if survived else proc.returncode)
