#!/usr/bin/env bash
# Runs the Unity editor headless from the GameCI Docker image against this project.
#
#   tools/unity.sh compile   compile-check the C# scripts (no license needed)
#   tools/unity.sh prepare   import characters from Assets/Characters and generate the scene
#   tools/unity.sh build     build WebGL into Build/WebGL
#   tools/unity.sh run ARGS  run the editor with custom arguments
#
# License (Unity needs one even in batch mode), from the environment:
#   UNITY_LICENSE                       contents of Unity_lic.ulf (raw XML or base64) — Personal
#   UNITY_SERIAL + UNITY_EMAIL + UNITY_PASSWORD                                        — Pro/Plus
set -euo pipefail

IMAGE="${UNITY_IMAGE:-unityci/editor:ubuntu-6000.0.84f1-webgl-3}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LICENSE_DIR="$ROOT/.unity-license"
EDITOR_LOG_DIR="$ROOT/Logs"

log() { printf '\033[1;34m[unity]\033[0m %s\n' "$*" >&2; }
die() { printf '\033[1;31m[unity]\033[0m %s\n' "$*" >&2; exit 1; }

ensure_docker() {
  if docker info >/dev/null 2>&1; then return; fi
  command -v dockerd >/dev/null || die "Docker is not installed."
  log "Starting the Docker daemon…"
  nohup dockerd >/tmp/dockerd.log 2>&1 &
  for _ in $(seq 1 30); do
    docker info >/dev/null 2>&1 && return
    sleep 1
  done
  die "Docker daemon did not start; see /tmp/dockerd.log"
}

ensure_image() {
  if docker image inspect "$IMAGE" >/dev/null 2>&1; then return; fi
  log "Pulling $IMAGE (about 7.5 GB, takes a few minutes)…"
  docker pull "$IMAGE" >/dev/null
}

docker_args() {
  local args=(--rm --network host -v "$ROOT:/project" -w /project)
  # Route the editor's HTTPS (licensing) through this machine's proxy and trust its CA.
  for name in HTTPS_PROXY HTTP_PROXY NO_PROXY https_proxy http_proxy no_proxy; do
    if [[ -n "${!name:-}" ]]; then args+=(-e "$name=${!name}"); fi
  done
  if [[ -n "${HTTPS_PROXY:-}" && -z "${HTTP_PROXY:-}" ]]; then args+=(-e "HTTP_PROXY=$HTTPS_PROXY" -e "http_proxy=$HTTPS_PROXY"); fi
  if [[ -f /etc/ssl/certs/ca-certificates.crt ]]; then
    args+=(-v /etc/ssl/certs:/etc/ssl/certs:ro -e SSL_CERT_FILE=/etc/ssl/certs/ca-certificates.crt)
  fi
  if [[ -f "$LICENSE_DIR/Unity_lic.ulf" ]]; then
    args+=(-v "$LICENSE_DIR:/license:ro")
  fi
  for name in UNITY_SERIAL UNITY_EMAIL UNITY_PASSWORD; do
    if [[ -n "${!name:-}" ]]; then args+=(-e "$name"); fi
  done
  printf '%s\n' "${args[@]}"
}

write_license() {
  rm -rf "$LICENSE_DIR"
  if [[ -n "${UNITY_LICENSE:-}" ]]; then
    mkdir -p "$LICENSE_DIR"
    chmod 700 "$LICENSE_DIR"
    if [[ "$UNITY_LICENSE" == *"<"* ]]; then
      printf '%s' "$UNITY_LICENSE" | tr -d '\r' >"$LICENSE_DIR/Unity_lic.ulf"
    else
      printf '%s' "$UNITY_LICENSE" | base64 -d >"$LICENSE_DIR/Unity_lic.ulf" 2>/dev/null \
        || die "UNITY_LICENSE is neither the .ulf XML nor valid base64."
    fi
    return
  fi
  if [[ -z "${UNITY_SERIAL:-}" ]]; then
    die "No Unity license. Set UNITY_LICENSE (Unity_lic.ulf contents) or UNITY_SERIAL/UNITY_EMAIL/UNITY_PASSWORD."
  fi
  [[ -n "${UNITY_EMAIL:-}" && -n "${UNITY_PASSWORD:-}" ]] || die "UNITY_SERIAL needs UNITY_EMAIL and UNITY_PASSWORD."
}

# Activates inside the container, runs the editor, then returns a serial license.
run_editor() {
  write_license
  mkdir -p "$EDITOR_LOG_DIR"
  local -a args
  mapfile -t args < <(docker_args)
  local script='
set -uo pipefail
ULF_DIR=/root/.local/share/unity3d/Unity
if [[ -f /license/Unity_lic.ulf ]]; then
  mkdir -p "$ULF_DIR" && cp /license/Unity_lic.ulf "$ULF_DIR/Unity_lic.ulf"
  unity-editor -nographics -quit -manualLicenseFile "$ULF_DIR/Unity_lic.ulf" -logFile /project/Logs/activation.log >/dev/null 2>&1 || true
elif [[ -n "${UNITY_SERIAL:-}" ]]; then
  unity-editor -nographics -quit -serial "$UNITY_SERIAL" -username "$UNITY_EMAIL" -password "$UNITY_PASSWORD" \
    -logFile /project/Logs/activation.log >/dev/null 2>&1 || true
fi
unity-editor -nographics -projectPath /project -logFile - "$@"
code=$?
if [[ -n "${UNITY_SERIAL:-}" ]]; then
  unity-editor -nographics -quit -returnlicense -username "$UNITY_EMAIL" -password "$UNITY_PASSWORD" -logFile /dev/null >/dev/null 2>&1 || true
fi
exit $code
'
  local status=0
  docker run "${args[@]}" "$IMAGE" bash -c "$script" unity "$@" 2>&1 | tee "$EDITOR_LOG_DIR/last-run.log" || status=$?
  rm -rf "$LICENSE_DIR"
  if grep -q "No valid Unity Editor license found" "$EDITOR_LOG_DIR/last-run.log"; then
    die "Unity rejected the license. Activation log: Logs/activation.log"
  fi
  return "$status"
}

compile_scripts() {
  local -a args
  mapfile -t args < <(docker_args)
  docker run "${args[@]}" "$IMAGE" bash -c '
set -e
E=/opt/unity/Editor/Data
NS=$(find "$E/NetStandard" -name netstandard.dll -path "*ref/2.1.0*" | head -1)
REFS=(-r:"$NS")
for f in "$E"/Managed/UnityEngine/*.dll; do REFS+=(-r:"$f"); done
CSC=("$E/NetCoreRuntime/dotnet" "$E/DotNetSdkRoslyn/csc.dll" -nologo -langversion:9 -target:library -warnaserror+ -nowarn:1701,1702)
OUT=$(mktemp -d)
mapfile -t RUNTIME < <(find Assets -name "*.cs" -not -path "*/Editor/*")
mapfile -t EDITOR < <(find Assets -name "*.cs" -path "*/Editor/*")
"${CSC[@]}" "${REFS[@]}" -out:"$OUT/Assembly-CSharp.dll" "${RUNTIME[@]}"
echo "runtime scripts: OK (${#RUNTIME[@]} files)"
"${CSC[@]}" "${REFS[@]}" -r:"$OUT/Assembly-CSharp.dll" -out:"$OUT/Assembly-CSharp-Editor.dll" "${EDITOR[@]}"
echo "editor scripts: OK (${#EDITOR[@]} files)"
'
}

command="${1:-}"
[[ -n "$command" ]] && shift
case "$command" in
  compile)
    ensure_docker; ensure_image; compile_scripts ;;
  prepare)
    ensure_docker; ensure_image
    run_editor -quit -executeMethod CharacterPlayground.EditorTools.PlaygroundBuild.PrepareFromCommandLine ;;
  build)
    ensure_docker; ensure_image
    run_editor -quit -buildTarget WebGL -executeMethod CharacterPlayground.EditorTools.PlaygroundBuild.BuildWebGLFromCommandLine "$@" ;;
  run)
    ensure_docker; ensure_image; run_editor "$@" ;;
  *)
    sed -n '2,11p' "$0" | sed 's/^# \{0,1\}//'
    exit 1 ;;
esac
