#!/usr/bin/env bash
# Runs the Unity editor headless from the GameCI Docker image against this project.
#
#   tools/unity.sh compile   compile-check the C# scripts (no license needed)
#   tools/unity.sh prepare   import characters from Assets/Characters and generate the scene
#   tools/unity.sh build     build WebGL into Build/WebGL
#   tools/unity.sh run ARGS  run the editor with custom arguments
#
# License (Unity needs one even in batch mode), from the environment:
#   UNITY_EMAIL + UNITY_PASSWORD                   Personal (free): a seat is taken per run and returned
#   UNITY_SERIAL + UNITY_EMAIL + UNITY_PASSWORD    Pro/Plus
#   UNITY_LICENSE                                  a .ulf file (raw XML or base64), Enterprise/legacy
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
  # Values are inherited from this environment, never written on the command line.
  for name in UNITY_SERIAL UNITY_EMAIL UNITY_PASSWORD; do
    if [[ -n "${!name:-}" ]]; then args+=(-e "$name"); fi
  done
  printf '%s\n' "${args[@]}"
}

check_license_settings() {
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
  if [[ -n "${UNITY_EMAIL:-}" && -n "${UNITY_PASSWORD:-}" ]]; then return; fi
  if [[ -n "${UNITY_SERIAL:-}" ]]; then die "UNITY_SERIAL needs UNITY_EMAIL and UNITY_PASSWORD."; fi
  die "No Unity license. Set UNITY_EMAIL and UNITY_PASSWORD (Unity account), plus UNITY_SERIAL for Pro."
}

# Activates inside the container, runs the editor, then gives the seat back.
run_editor() {
  check_license_settings
  mkdir -p "$EDITOR_LOG_DIR"
  local -a args
  mapfile -t args < <(docker_args)
  local script='
set -uo pipefail
CLIENT=/opt/unity/Editor/Data/Resources/Licensing/Client/Unity.Licensing.Client
ACTIVATION_LOG=/project/Logs/activation.log
: >"$ACTIVATION_LOG"
HAVE_ACCOUNT=false
[[ -n "${UNITY_EMAIL:-}" && -n "${UNITY_PASSWORD:-}" ]] && HAVE_ACCOUNT=true

activate_personal() {
  local attempt out code
  for attempt in 1 2 3; do
    out=$("$CLIENT" --activate-all --include-personal --username "$UNITY_EMAIL" --password "$UNITY_PASSWORD" 2>&1)
    code=$?
    printf "%s\n" "$out" >>"$ACTIVATION_LOG"
    # The client exits 0 even when no seat was assigned.
    if [[ $code -eq 0 ]] && ! grep -qiE "No seat available|No license activation found" <<<"$out"; then
      return 0
    fi
    grep -qiE "invalid (credentials|password|username)|unauthori[sz]ed|401" <<<"$out" && return 1
    sleep $((attempt * 10))
  done
  return 1
}

return_personal() {
  local out
  out=$("$CLIENT" --return-ulf 2>&1)
  printf "%s\n" "$out" >>"$ACTIVATION_LOG"
  if grep -qi "not found" <<<"$out"; then
    unity-editor -nographics -quit -returnlicense -username "$UNITY_EMAIL" -password "$UNITY_PASSWORD" \
      -logFile - >>"$ACTIVATION_LOG" 2>&1 || true
  fi
}

METHOD=none
if [[ -n "${UNITY_SERIAL:-}" ]]; then
  METHOD=serial
  unity-editor -nographics -quit -serial "$UNITY_SERIAL" -username "$UNITY_EMAIL" -password "$UNITY_PASSWORD" \
    -logFile - >>"$ACTIVATION_LOG" 2>&1 || true
elif [[ -f /license/Unity_lic.ulf ]]; then
  METHOD=file
  ULF_DIR=/root/.local/share/unity3d/Unity
  mkdir -p "$ULF_DIR" && cp /license/Unity_lic.ulf "$ULF_DIR/Unity_lic.ulf"
  unity-editor -nographics -quit -manualLicenseFile "$ULF_DIR/Unity_lic.ulf" -logFile - >>"$ACTIVATION_LOG" 2>&1 || true
  # A .ulf is bound to the machine that requested it; use the account instead when it does not match.
  if grep -q "Machine bindings don.t match" "$ACTIVATION_LOG" && $HAVE_ACCOUNT; then METHOD=personal; fi
elif $HAVE_ACCOUNT; then
  METHOD=personal
fi

if [[ $METHOD == personal ]] && ! activate_personal; then
  echo "[unity] Personal license activation failed; see Logs/activation.log" >&2
  return_personal
  exit 3
fi

unity-editor -nographics -projectPath /project -logFile - "$@"
code=$?

case $METHOD in
  personal) return_personal ;;
  serial) unity-editor -nographics -quit -returnlicense -username "$UNITY_EMAIL" -password "$UNITY_PASSWORD" \
            -logFile - >>"$ACTIVATION_LOG" 2>&1 || true ;;
esac
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
    sed -n '2,12p' "$0" | sed 's/^# \{0,1\}//'
    exit 1 ;;
esac
