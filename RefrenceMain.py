import sys
import os
import json
import re
import time
import zipfile
import subprocess
from pathlib import Path

from PyQt6.QtCore import Qt, QThread, pyqtSignal, QUrl
from PyQt6.QtWidgets import (
    QApplication, QMainWindow, QWidget, QVBoxLayout, QLabel,
    QPushButton, QProgressBar, QHBoxLayout, QTabWidget,
    QDialog, QFrame, QMessageBox
)
from PyQt6.QtWebEngineWidgets import QWebEngineView
from PyQt6.QtWebEngineCore import QWebEngineDownloadRequest

import win32com.client

# ================= CONFIG =================
DEFAULT_INSTALL = Path("C:/games/AnkerGames")
APPDATA = Path(os.getenv("APPDATA")) / "AnkerGamesClient"
SETTINGS_FILE = APPDATA / "settings.json"
LIBRARY_FILE = DEFAULT_INSTALL / "library.json"
SEVEN_ZIP = Path("C:/Program Files/7-Zip/7z.exe")
QBITTORRENT = Path("C:/Program Files/qBittorrent/qbittorrent.exe")


# ================= SETTINGS =================
def load_settings():
    APPDATA.mkdir(parents=True, exist_ok=True)
    if SETTINGS_FILE.exists():
        return json.loads(SETTINGS_FILE.read_text())
    return {
        "install_path": str(DEFAULT_INSTALL),
        "desktop_shortcut": True,
        "start_shortcut": True
    }


# ================= CURL DOWNLOAD WORKER =================
class CurlWorker(QThread):
    progress = pyqtSignal(int)       # 0-100
    speed_update = pyqtSignal(str)   # "3.24 MB/s"
    status_update = pyqtSignal(str)  # status text
    finished = pyqtSignal(str)       # save_path on success, "" on fail

    def __init__(self, url, save_path, cookies=""):
        super().__init__()
        self.url = url
        self.save_path = str(save_path)
        self.cookies = cookies
        self._cancel = False
        self._proc = None

    def run(self):
        self.status_update.emit("Downloading")

        cmd = [
            "curl",
            "--location",           # follow redirects
            "--progress-bar",       # simple progress output
            "--write-out", "%{speed_download}|%{size_download}|%{http_code}",
            "--output", self.save_path,
            "--user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36",
        ]

        if self.cookies:
            cmd += ["--cookie", self.cookies]

        cmd.append(self.url)

        try:
            self._proc = subprocess.Popen(
                cmd,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True
            )

            # Read stderr for progress (curl writes progress to stderr)
            last_bytes = 0
            last_time = time.time()

            while True:
                if self._cancel:
                    self._proc.terminate()
                    self.status_update.emit("Cancelled")
                    return

                line = self._proc.stderr.readline()
                if not line and self._proc.poll() is not None:
                    break

                # Parse curl progress line: looks like "  % Total    % Received ..."
                # curl --progress-bar outputs lines like: "##...  xx%"
                # We parse the numeric progress
                match = re.search(r'(\d+\.\d+|\d+)%', line)
                if match:
                    pct = min(int(float(match.group(1))), 100)
                    self.progress.emit(pct)

                # Speed from bytes received change
                save = Path(self.save_path)
                if save.exists():
                    now = time.time()
                    current_bytes = save.stat().st_size
                    dt = now - last_time
                    if dt >= 0.5:
                        delta = current_bytes - last_bytes
                        spd = delta / dt / 1024 / 1024
                        self.speed_update.emit(f"{spd:.2f} MB/s")
                        last_bytes = current_bytes
                        last_time = now

            stdout, _ = self._proc.communicate()

            # Parse write-out: speed|size|http_code
            parts = stdout.strip().split("|")
            if len(parts) == 3:
                http_code = parts[2].strip()
                if http_code not in ("200", "206"):
                    self.status_update.emit(f"HTTP Error {http_code}")
                    self.finished.emit("")
                    return

            self.progress.emit(100)
            self.status_update.emit("Done")
            self.finished.emit(self.save_path)

        except FileNotFoundError:
            self.status_update.emit("curl not found — install curl")
            self.finished.emit("")
        except Exception as e:
            self.status_update.emit(f"Error: {e}")
            self.finished.emit("")

    def cancel(self):
        self._cancel = True
        if self._proc:
            self._proc.terminate()


# ================= DOWNLOAD CENTER =================
class DownloadCenter(QDialog):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("Download Center")
        self.resize(700, 500)
        self.layout = QVBoxLayout(self)
        self.workers = []  # Keep references to prevent GC

    def add_curl_download(self, url, filename, install_path, cookies, callback):
        save_path = Path(install_path) / filename

        row = QFrame()
        row_layout = QVBoxLayout(row)

        label = QLabel(filename)
        progress = QProgressBar()
        speed = QLabel("Speed: -- MB/s")
        status = QLabel("Starting...")

        btn_cancel = QPushButton("Cancel")

        row_layout.addWidget(label)
        row_layout.addWidget(progress)
        row_layout.addWidget(speed)
        row_layout.addWidget(status)
        row_layout.addWidget(btn_cancel)

        self.layout.addWidget(row)

        worker = CurlWorker(url, save_path, cookies)
        self.workers.append(worker)

        worker.progress.connect(progress.setValue)
        worker.speed_update.connect(speed.setText)
        worker.status_update.connect(status.setText)

        def on_finished(path):
            if worker in self.workers:
                self.workers.remove(worker)
            if path:
                self.handle_extract(path, filename, status, callback)

        worker.finished.connect(on_finished)

        def cancel():
            worker.cancel()
            status.setText("Cancelled")

        btn_cancel.clicked.connect(cancel)

        worker.start()

    def handle_extract(self, file_path, name, status, callback):
        status.setText("Extracting...")
        file_path = Path(file_path)
        extract_path = file_path.with_suffix("")
        extract_path.mkdir(parents=True, exist_ok=True)

        ok = self.extract_archive(file_path, extract_path)
        if not ok:
            status.setText("Extract Error — check 7-Zip")
            return

        exe = self.find_exe(extract_path)
        status.setText("✔ Done")
        callback(name, str(exe) if exe else "")

    def extract_archive(self, file_path, extract_path):
        """Extract ZIP or RAR/7z using 7-Zip."""
        fp = str(file_path)
        seven_zip = str(SEVEN_ZIP) if SEVEN_ZIP.exists() else "7z"

        if fp.endswith(".zip"):
            try:
                with zipfile.ZipFile(fp, 'r') as z:
                    z.extractall(extract_path)
                return True
            except Exception as e:
                print(f"ZIP error: {e}")
                return False

        # RAR, 7z, or anything else — let 7-Zip handle it
        try:
            subprocess.run(
                [seven_zip, "x", fp, f"-o{extract_path}", "-y"],
                check=True, capture_output=True
            )
            return True
        except Exception as e:
            print(f"7zip error: {e}")
            return False

    def find_exe(self, folder):
        exes = [e for e in folder.rglob("*.exe")
                if not any(x in e.name.lower() for x in ["setup", "unins", "redist"])]
        return max(exes, key=lambda x: x.stat().st_size) if exes else None


# ================= LIBRARY =================
class LibraryWidget(QWidget):
    def __init__(self):
        super().__init__()
        self.layout = QVBoxLayout(self)
        self.load()

    def load(self):
        if not LIBRARY_FILE.exists():
            LIBRARY_FILE.parent.mkdir(parents=True, exist_ok=True)
            LIBRARY_FILE.write_text("[]")

        data = json.loads(LIBRARY_FILE.read_text())

        if not data:
            self.layout.addWidget(QLabel("No games installed yet"))
            return

        for g in data:
            self.add_card(g)

    def add_game(self, name, exe):
        data = json.loads(LIBRARY_FILE.read_text())
        data.append({"name": name, "exe": exe})
        LIBRARY_FILE.write_text(json.dumps(data, indent=4))
        self.add_card({"name": name, "exe": exe})

    def add_card(self, game):
        card = QFrame()
        layout = QVBoxLayout(card)

        layout.addWidget(QLabel(game["name"]))

        btn_launch = QPushButton("Launch")
        btn_delete = QPushButton("Delete")

        btn_launch.clicked.connect(lambda: subprocess.Popen(game["exe"]))

        def delete():
            data = json.loads(LIBRARY_FILE.read_text())
            data = [x for x in data if x["name"] != game["name"]]
            LIBRARY_FILE.write_text(json.dumps(data, indent=4))
            card.deleteLater()

        btn_delete.clicked.connect(delete)

        layout.addWidget(btn_launch)
        layout.addWidget(btn_delete)

        self.layout.addWidget(card)


# ================= MAIN WINDOW =================
class MainWindow(QMainWindow):
    def __init__(self):
        super().__init__()

        self.setWindowTitle("AnkerGames Client")
        self.resize(1280, 800)

        self.settings = load_settings()

        self.tabs = QTabWidget()
        self.setCentralWidget(self.tabs)

        # Browser
        self.web = QWebEngineView()
        self.web.setUrl(QUrl("https://ankergames.net"))
        profile = self.web.page().profile()
        profile.downloadRequested.connect(self.handle_download)

        self.tabs.addTab(self.web, "Browse")

        # Library
        self.library = LibraryWidget()
        self.tabs.addTab(self.library, "Library")

        # Downloads
        self.download_center = DownloadCenter()

    def handle_download(self, download):
        url = download.url().toString()
        print("Download request:", url)

        # Torrent / magnet → qBittorrent
        if url.startswith("magnet:") or url.endswith(".torrent"):
            download.cancel()
            if QBITTORRENT.exists():
                subprocess.Popen([str(QBITTORRENT), url])
            else:
                QMessageBox.warning(self, "Not Found", "qBittorrent not found at:\n" + str(QBITTORRENT))
            return

        # Cancel Qt's own slow downloader
        download.cancel()

        # Get filename
        filename = download.downloadFileName() or ""
        filename = filename.split("?")[0]
        if not filename or "." not in filename:
            filename = Path(url).name.split("?")[0] or "game"
        if "." not in filename:
            filename += ".rar"

        # Grab cookies from the current page session to pass to curl
        cookies = self._get_cookies()

        install_path = Path(self.settings["install_path"])
        install_path.mkdir(parents=True, exist_ok=True)

        self.download_center.show()
        self.download_center.add_curl_download(
            url, filename, install_path, cookies,
            self.on_download_complete
        )

    def _get_cookies(self):
        """Extract cookies from WebEngine session as a cookie string for curl."""
        # We pass the raw cookie header string; collected via JS
        # This is best-effort — works for session cookies
        cookies = []
        self.web.page().runJavaScript(
            "document.cookie",
            lambda c: cookies.append(c or "")
        )
        # Give JS a moment to respond (synchronous-ish via list)
        time.sleep(0.1)
        return cookies[0] if cookies else ""

    def on_download_complete(self, name, exe):
        if exe:
            self.library.add_game(name, exe)
            self.create_shortcut(exe, name)

    def create_shortcut(self, target, name):
        shell = win32com.client.Dispatch("WScript.Shell")
        desktop = Path(os.path.join(os.environ["USERPROFILE"], "Desktop"))
        shortcut = shell.CreateShortcut(str(desktop / f"{name}.lnk"))
        shortcut.Targetpath = target
        shortcut.save()


# ================= APP =================
def main():
    app = QApplication(sys.argv)

    app.setStyleSheet("""
    QWidget { background:#0f0f13; color:white; }
    QPushButton { background:#1a1a24; border-radius:6px; padding:6px; }
    """)

    win = MainWindow()
    win.show()

    sys.exit(app.exec())


if __name__ == "__main__":
    main()
