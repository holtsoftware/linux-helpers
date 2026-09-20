#!/usr/bin/env bash
# Publishes folder-icons as a self-contained, ReadyToRun, trimmed single-file executable to ~/.local/bin and
# (re)starts its systemd user service. No .NET runtime is needed to run the result.
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

case "$(uname -m)" in
    x86_64)  rid=linux-x64 ;;
    aarch64) rid=linux-arm64 ;;
    *) echo "unsupported architecture: $(uname -m)" >&2; exit 1 ;;
esac

out="$(mktemp -d)"
trap 'rm -rf "$out"' EXIT

dotnet publish "$here/.." -c Release -r "$rid" --self-contained \
    -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:PublishTrimmed=true -p:TrimMode=partial \
    -p:SuppressTrimAnalysisWarnings=true -p:DebugType=none \
    -o "$out"

install -Dm755 "$out/folder-icons" "$HOME/.local/bin/folder-icons"
install -Dm644 "$here/folder-icons.service" "$HOME/.config/systemd/user/folder-icons.service"
[ -e "$HOME/.config/folder-icons.yaml" ] || install -Dm644 "$here/folder-icons.example.yaml" "$HOME/.config/folder-icons.yaml"

systemctl --user daemon-reload
systemctl --user enable folder-icons.service
systemctl --user restart folder-icons.service
