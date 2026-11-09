#!/usr/bin/env bash
# Smoke test against a real Umbraco. Boots the test site on a throwaway SQLite database, runs the health
# checks and notification methods through the dev-only endpoint, and inspects the emails Umbraco writes
# to a pickup folder and the webhook calls a local listener receives.
# Usage: bash tests/smoke/run.sh   (needs dotnet 10, python3 and curl)
set -u

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SITE="$ROOT/tests/Simplepage.Umbraco.HealthCheckAlerts.TestSite"
WORK="$(mktemp -d)"
FAILS=0

free_port() { python3 -c 'import socket;s=socket.socket();s.bind(("127.0.0.1",0));print(s.getsockname()[1]);s.close()'; }
SITE_PORT="$(free_port)"
HOOK_PORT="$(free_port)"

stop_port() {
  local pids
  pids="$(lsof -ti :"$1" 2>/dev/null || true)"
  if [ -n "$pids" ]; then kill $pids 2>/dev/null; fi
  return 0
}
cleanup() {
  stop_port "$SITE_PORT"; stop_port "$HOOK_PORT"
  sleep 1
  if lsof -ti :"$SITE_PORT" >/dev/null 2>&1 || lsof -ti :"$HOOK_PORT" >/dev/null 2>&1; then echo "WARNING: something is still listening"; fi
  rm -rf "$WORK"
}
trap cleanup EXIT

ok()    { echo "  ok    $1"; }
fail()  { echo "  FAIL  $1"; FAILS=$((FAILS + 1)); }
check() { if eval "$2"; then ok "$1"; else fail "$1"; fi; }

# A tiny webhook receiver: appends each call to hooks.jsonl with the auth header it carried.
cat > "$WORK/hook.py" <<'PY'
import http.server, json, sys
out = sys.argv[2]
class H(http.server.BaseHTTPRequestHandler):
    def do_POST(self):
        body = self.rfile.read(int(self.headers.get("Content-Length", 0))).decode()
        with open(out, "a") as f:
            f.write(json.dumps({"key": self.headers.get("X-Api-Key"), "body": body}) + "\n")
        self.send_response(204); self.end_headers()
    def log_message(self, *a): pass
http.server.HTTPServer(("127.0.0.1", int(sys.argv[1])), H).serve_forever()
PY
python3 "$WORK/hook.py" "$HOOK_PORT" "$WORK/hooks.jsonl" &

echo "Building the test site"
dotnet build "$SITE" -c Release --nologo -v q >"$WORK/build.log" 2>&1 || { cat "$WORK/build.log"; exit 1; }

# Settings go in as environment variables, the same route a Cloud site uses for its secrets.
P="Umbraco__CMS__HealthChecks__Notification__NotificationMethods__problemsEmail__Settings"
export ConnectionStrings__umbracoDbDSN="Data Source=$WORK/umbraco.sqlite.db;Cache=Shared;Foreign Keys=True;Pooling=True"
export ConnectionStrings__umbracoDbDSN_ProviderName="Microsoft.Data.Sqlite"
export Umbraco__CMS__Global__Smtp__PickupDirectoryLocation="$WORK/mail"
export Umbraco__CMS__Imaging__HMACSecretKey="$(python3 -c 'import base64,os;print(base64.b64encode(os.urandom(64)).decode())')"
export HEALTHALERTS_TEST_ENDPOINT=1
export ASPNETCORE_URLS="http://127.0.0.1:$SITE_PORT"
mkdir -p "$WORK/mail"

start_site() { # $1 = environment, the rest are extra NAME=value settings
  local envname="$1"; shift
  (cd "$SITE" && env ASPNETCORE_ENVIRONMENT="$envname" "$@" dotnet bin/Release/net10.0/Simplepage.Umbraco.HealthCheckAlerts.TestSite.dll >"$WORK/site-$envname.log" 2>&1) &
  for _ in $(seq 1 180); do
    if curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:$SITE_PORT/" | grep -qE '^(200|302|404)$'; then return 0; fi
    sleep 1
  done
  echo "Site did not start:"; tail -30 "$WORK/site-$envname.log"; return 1
}
run_checks() { curl -s -X POST "http://127.0.0.1:$SITE_PORT/dev/run-health-notifications"; }
emls()       { find "$WORK/mail" -name '*.eml' 2>/dev/null | wc -l | tr -d ' '; }
hooks()      { if [ -f "$WORK/hooks.jsonl" ]; then wc -l < "$WORK/hooks.jsonl" | tr -d ' '; else echo 0; fi; }
latest_eml() { ls -t "$WORK"/mail/*.eml | head -1; }

echo
echo "A: Development sends nothing by default"
start_site Development || exit 1
check "the run endpoint answers" 'run_checks | grep -qi problemsEmail'
check "no email in Development" '[ "$(emls)" = 0 ]'
stop_port "$SITE_PORT"; sleep 3

echo
echo "B: Development with ignoring switched off, a webhook, and a repeat run"
start_site Development "${P}__IgnoreEnvironments=None" "${P}__WebhookUrl=http://127.0.0.1:$HOOK_PORT/hook" "${P}__WebhookAuthKey=smoke-key" || exit 1
run_checks >/dev/null
check "one email was written" '[ "$(emls)" = 1 ]'
EML="$(latest_eml)"
check "it went to both recipients" 'grep -qi "one@example.test" "$EML" && grep -qi "two@example.test" "$EML"'
check "the subject names the site and environment" 'grep -qi "^Subject:.*Test Site (Development)" "$EML"'
check "the subject is a problems subject" 'grep -qiE "^Subject:.*(error|warning)" "$EML"'
check "one webhook call was made" '[ "$(hooks)" = 1 ]'
check "the webhook got the auth key" 'grep -q "\"key\": \"smoke-key\"" "$WORK/hooks.jsonl"'
check "the webhook body is the problems payload" 'python3 - "$WORK/hooks.jsonl" <<PY
import json, sys
b = json.loads(json.loads(open(sys.argv[1]).readline())["body"])
assert b["status"] == "problems" and b["site"] == "Test Site (Development)" and len(b["problems"]) > 0
PY'
run_checks >/dev/null
check "a second run with the same problems sends nothing more" '[ "$(emls)" = 1 ] && [ "$(hooks)" = 1 ]'
stop_port "$SITE_PORT"; sleep 3

echo
echo "C: after a restart the same problems are still not repeated, even in another environment"
BEFORE="$(emls)"
start_site Staging || exit 1
run_checks >/dev/null
check "no new email: the record of what was reported survived the restart" '[ "$(emls)" = "$BEFORE" ]'
stop_port "$SITE_PORT"; sleep 3

echo
echo "D: Staging is not ignored, and EveryRun repeats unchanged problems"
start_site Staging "${P}__RepeatMode=EveryRun" || exit 1
run_checks >/dev/null
check "a new email was written" '[ "$(emls)" -gt "$BEFORE" ]'
check "its subject says Staging" 'grep -qi "^Subject:.*Test Site (Staging)" "$(latest_eml)"'
stop_port "$SITE_PORT"; sleep 2

echo
if [ "$FAILS" -gt 0 ]; then echo "$FAILS smoke check(s) failed"; exit 1; fi
echo "Smoke test passed"
