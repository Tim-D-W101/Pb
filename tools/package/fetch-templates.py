#!/usr/bin/env python3
"""Installs single Godot .NET export templates without downloading the whole 1.2 GB archive.

The release's .tpz is a zip, so HTTP range requests can read its directory and just the entries
asked for. They go where Godot looks for them (~/.local/share/godot/export_templates/<version>/).

    tools/package/fetch-templates.py 4.7.2 windows_release_x86_64.exe windows_release_x86_64_console.exe
"""
import io
import os
import sys
import urllib.request
import zipfile


class RangeFile(io.RawIOBase):
    """A read-only, seekable view of a remote file, fetched by HTTP range requests."""

    def __init__(self, url):
        with urllib.request.urlopen(urllib.request.Request(url, method="HEAD")) as response:
            self.url = response.geturl()  # the signed download address the release redirects to
            self.size = int(response.headers["Content-Length"])
        self.pos = 0

    def readable(self):
        return True

    def seekable(self):
        return True

    def tell(self):
        return self.pos

    def seek(self, offset, whence=io.SEEK_SET):
        base = {io.SEEK_SET: 0, io.SEEK_CUR: self.pos, io.SEEK_END: self.size}[whence]
        self.pos = base + offset
        return self.pos

    def readinto(self, buffer):
        if self.pos >= self.size:
            return 0
        end = min(self.size, self.pos + len(buffer)) - 1
        request = urllib.request.Request(self.url, headers={"Range": f"bytes={self.pos}-{end}"})
        with urllib.request.urlopen(request) as response:
            data = response.read()
        buffer[: len(data)] = data
        self.pos += len(data)
        return len(data)


def main():
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    version, wanted = sys.argv[1], sys.argv[2:]
    url = (f"https://github.com/godotengine/godot/releases/download/{version}-stable/"
           f"Godot_v{version}-stable_mono_export_templates.tpz")
    archive = zipfile.ZipFile(io.BufferedReader(RangeFile(url), buffer_size=8 << 20))
    stamp = archive.read("templates/version.txt").decode().strip()  # e.g. 4.7.2.stable.mono
    data_home = os.environ.get("XDG_DATA_HOME") or os.path.expanduser("~/.local/share")
    dest = os.path.join(data_home, "godot", "export_templates", stamp)
    os.makedirs(dest, exist_ok=True)
    with open(os.path.join(dest, "version.txt"), "w") as out:
        out.write(stamp)
    for name in wanted:
        target = os.path.join(dest, name)
        with archive.open(f"templates/{name}") as source, open(target, "wb") as out:
            while chunk := source.read(8 << 20):
                out.write(chunk)
        os.chmod(target, 0o755)
        print(f"{stamp}: {name} ({os.path.getsize(target) / 1048576:.0f} MB) → {dest}")


if __name__ == "__main__":
    main()
