#!/usr/bin/env bash
# Quick connectivity check for the bank ↔ KLIK (BLIK P2P) integration.
# Designed for the MarshallBjorn team — no bank API knowledge needed.
#
# All checks are read-only — no aliases are created, no state is modified.
# The alias lookup uses a dummy phone number (+00000000000) that will return
# 404 "not found" — this confirms API key authentication works without
# any side effects.
#
# Usage:
#   bash verify-blik-integration.sh
#
# Reads credentials from .env in the same directory.
# Requires: curl, python3

set -euo pipefail
cd "$(dirname "$0")/.."

# ── load .env ──────────────────────────────────────────────────────────────────
if [[ ! -f .env ]]; then
  echo "ERROR: .env not found in $(pwd). Copy .env.example and fill in values."
  exit 1
fi
set -a; source .env; set +a

BANK_URL="${API_URL:-http://localhost:5100}"
KLIK_URL="${KLIK_URL:-http://localhost:8000}"
KLIK_API_KEY="${KLIK_API_KEY:?KLIK_API_KEY not set in .env}"

ok=0; fail=0; total=0

check() {
  local desc="$1" got="$2" want="$3"
  total=$((total+1))
  if echo "$got" | grep -qi "$want"; then
    printf "  \033[32mPASS\033[0m  %s\n" "$desc"; ok=$((ok+1))
  else
    printf "  \033[31mFAIL\033[0m  %s\n         got:  %s\n         want: %s\n" "$desc" "$got" "$want"; fail=$((fail+1))
  fi
}

echo "╔══════════════════════════════════════════════════════════╗"
echo "║  Bank ↔ KLIK (BLIK P2P)  Integration Verification       ║"
echo "╚══════════════════════════════════════════════════════════╝"
echo ""

# ── 1. Health checks ──────────────────────────────────────────────────────────
echo "▶ 1. Health checks"
BANK_HEALTH=$(curl -s -o /dev/null -w "%{http_code}" "$BANK_URL/health" 2>/dev/null || echo "000")
check "Bank API ($BANK_URL)" "$BANK_HEALTH" "200"

KLIK_HEALTH=$(curl -s "$KLIK_URL/healthz/" 2>/dev/null || echo "{}")
KLIK_STATUS=$(echo "$KLIK_HEALTH" | python3 -c "import sys,json; print(json.load(sys.stdin).get('status',''))" 2>/dev/null || echo "")
check "KLIK System ($KLIK_URL)" "$KLIK_STATUS" "healthy"

KLIK_DB=$(echo "$KLIK_HEALTH" | python3 -c "import sys,json; print(json.load(sys.stdin).get('checks',{}).get('database',{}).get('ok',''))" 2>/dev/null || echo "")
check "KLIK database" "$KLIK_DB" "True"

KLIK_CACHE=$(echo "$KLIK_HEALTH" | python3 -c "import sys,json; print(json.load(sys.stdin).get('checks',{}).get('cache',{}).get('ok',''))" 2>/dev/null || echo "")
check "KLIK cache" "$KLIK_CACHE" "True"

# ── 2. API key authentication ────────────────────────────────────────────────
echo ""
echo "▶ 2. API key handshake (read-only alias lookup)"
# Lookup a dummy phone number — expects 404 (alias not found).
# 404 confirms the API key was accepted; 401/403 means the key is wrong.
LOOKUP_RESP=$(curl -s -w "\n%{http_code}" \
  "$KLIK_URL/api/v1/aliases/lookup/%2B00000000000" \
  -H "X-KLIK-Bank-Api-Key: $KLIK_API_KEY" 2>/dev/null)
LOOKUP_HTTP=$(echo "$LOOKUP_RESP" | tail -1)
LOOKUP_BODY=$(echo "$LOOKUP_RESP" | sed '$d')

if [[ "$LOOKUP_HTTP" == "404" ]]; then
  LOOKUP_CODE=$(echo "$LOOKUP_BODY" | python3 -c "import sys,json; print(json.load(sys.stdin).get('error',{}).get('code',''))" 2>/dev/null || echo "")
  check "API key accepted (404 = alias not found, auth OK)" "$LOOKUP_CODE" "404_ALIAS_NOT_FOUND"
elif [[ "$LOOKUP_HTTP" == "401" || "$LOOKUP_HTTP" == "403" ]]; then
  check "API key accepted" "HTTP $LOOKUP_HTTP (auth rejected)" "404"
else
  check "API key accepted" "HTTP $LOOKUP_HTTP" "404"
fi

BAD_KEY_HTTP=$(curl -s -o /dev/null -w "%{http_code}" \
  "$KLIK_URL/api/v1/aliases/lookup/%2B00000000000" \
  -H "X-KLIK-Bank-Api-Key: invalid-key-00000" 2>/dev/null || echo "000")
if [[ "$BAD_KEY_HTTP" == "401" || "$BAD_KEY_HTTP" == "403" ]]; then
  check "Reject invalid API key (expect 401/403)" "rejected ($BAD_KEY_HTTP)" "rejected"
else
  check "Reject invalid API key (expect 401/403)" "HTTP $BAD_KEY_HTTP" "401"
fi

# ── 3. Bank webhook reachability ─────────────────────────────────────────────
echo ""
echo "▶ 3. Bank webhook endpoint (KLIK → Bank)"
WEBHOOK_HTTP=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$BANK_URL/klik/webhook/ping" \
  -H "Content-Type: application/json" \
  -H "X-Webhook-Secret: ${KLIK_WEBHOOK_SECRET:-none}" \
  -d '{"timestamp":"2026-01-01T00:00:00Z","nonce":"verify-test"}' 2>/dev/null || echo "000")
check "POST /klik/webhook/ping reachable" "$WEBHOOK_HTTP" "200"

# ── Summary ───────────────────────────────────────────────────────────────────
echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
if [[ $fail -eq 0 ]]; then
  printf "\033[32m  ALL %d CHECKS PASSED\033[0m\n" "$total"
else
  printf "\033[31m  %d/%d FAILED\033[0m\n" "$fail" "$total"
fi
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
exit $fail
