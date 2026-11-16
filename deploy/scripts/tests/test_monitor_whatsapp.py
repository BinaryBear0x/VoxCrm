"""Offline monitor tests: docker, locking and notification delivery are mocked.

Run: BASH_EXE=/path/to/bash python -m unittest discover -s deploy/scripts/tests -v
No Docker daemon, Telegram or production server is contacted.
"""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time
import unittest


class WhatsAppMonitorTests(unittest.TestCase):
    clinic = "11111111-1111-1111-1111-111111111111"
    other = "22222222-2222-2222-2222-222222222222"

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="voxcrm-monitor-test-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.script = self.root / "monitor-whatsapp.sh"
        self.script.write_text((Path(__file__).parents[1] / "monitor-whatsapp.sh").read_text(encoding="utf-8"), encoding="utf-8", newline="\n")
        notify = self.root / "notify.sh"
        notify.write_text('#!/usr/bin/env bash\nprintf "%s|%s\\n" "$2" "$1" >> "$TEST_NOTIFICATIONS"\n', encoding="utf-8", newline="\n")
        notify.chmod(0o755)
        self.notifications = self.root / "notifications.txt"
        self.state = self.root / "state"
        self.bash = os.environ.get("BASH_EXE") or shutil.which("bash")
        if not self.bash:
            self.skipTest("Bash required")

    def run_monitor(self, rows, success=True):
        env = os.environ.copy()
        env.update(TEST_METRICS="\n".join(rows), TEST_SCRIPT=self.script.as_posix(),
                   TEST_NOTIFICATIONS=self.notifications.as_posix(), MONITOR_STATE_DIR=self.state.as_posix(),
                   QUEUE_LAG_THRESHOLD_SECONDS="300")
        wrapper = '''docker() { printf '%s\\n' "$TEST_METRICS"; }
flock() { return 0; }
source "$TEST_SCRIPT" mock-postgres
'''
        result = subprocess.run([self.bash, "-c", wrapper], env=env, capture_output=True, text=True, encoding="utf-8", timeout=15)
        if success:
            self.assertEqual(result.returncode, 0, result.stderr)
        else:
            self.assertNotEqual(result.returncode, 0)
        return self.notifications.read_text(encoding="utf-8").splitlines() if self.notifications.exists() else []

    def test_offline_emits_only_one_disconnect_per_outage(self):
        first = self.run_monitor([f"{self.clinic}|disconnected|1|0|20880"])
        self.assertEqual(len(first), 1)
        self.assertIn("cihaz bağlantısı kesildi", first[0])
        self.assertNotIn("queue gecikmesi:", first[0])
        self.assertNotIn("retry bekleyen mesaj:", first[0])
        for status in ("disconnected", "qr", "connecting", "auth_failed"):
            self.assertEqual(self.run_monitor([f"{self.clinic}|{status}|5|2|30000"]), first)

    def test_reconnect_and_stuck_queue_are_transition_alerts(self):
        self.run_monitor([f"{self.clinic}|disconnected|1|0|20880"])
        messages = self.run_monitor([f"{self.clinic}|ready|1|0|20880"])
        self.assertEqual(len(messages), 3)  # disconnect, reconnect, one due-retry warning
        self.assertIn("yeniden kuruldu", messages[1])
        self.assertIn("retry bekleyen mesaj: 1", messages[2])
        self.assertEqual(self.run_monitor([f"{self.clinic}|ready|1|0|22000"]), messages)
        state = self.state / f"whatsapp-{self.clinic}.state"
        state.write_text(f"ready|1|0|0|{int(time.time()) - 1000}\n", encoding="utf-8", newline="\n")
        messages = self.run_monitor([f"{self.clinic}|ready|1|0|22000"])
        self.assertEqual(len(messages), 4)
        self.assertIn("kuyruk ilerlemiyor", messages[-1])
        self.assertEqual(self.run_monitor([f"{self.clinic}|ready|1|0|23000"]), messages)
        self.run_monitor([f"{self.clinic}|ready|0|0|0"])
        self.assertEqual(len(self.run_monitor([f"{self.clinic}|ready|1|0|500"])), 6)

    def test_each_clinic_is_gated_independently(self):
        messages = self.run_monitor([f"{self.clinic}|disconnected|20|0|30000", f"{self.other}|ready|3|0|1000"])
        self.assertEqual(len(messages), 2)
        self.assertIn(self.clinic, messages[0])
        self.assertIn("retry bekleyen mesaj: 3", messages[1])
        self.assertIn(self.other, messages[1])
        self.assertFalse(any("retry bekleyen mesaj: 20" in m for m in messages))

    def test_invalid_metrics_do_not_change_persisted_state(self):
        self.run_monitor([f"{self.clinic}|disconnected|1|0|20000"])
        state = self.state / f"whatsapp-{self.clinic}.state"
        previous = state.read_text(encoding="utf-8")
        self.run_monitor([f"{self.clinic}|ready|invalid|0|300"], success=False)
        self.assertEqual(state.read_text(encoding="utf-8"), previous)


if __name__ == "__main__":
    unittest.main()
