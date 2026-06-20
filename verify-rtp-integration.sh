#!/usr/bin/env bash
# Quick connectivity check for the bank ↔ TCHSystems RTP integration.
# Designed for the VanillaMile team — no bank API knowledge needed.
#
# All checks are read-only — no transfers are created, no state is modified.
#
# Usage:
#   bash verify-rtp-integration.sh
#
# Reads credentials from .env in the same directory.
# Requires: curl, python3

set -uo pipefail
cd "$(dirname "$0")"

# ── load .env ──────────────────────────────────────────────────────────────────
if [[ ! -f .env ]]; then
  echo "ERROR: .env not found in $(pwd). Copy .env.example and fill in values."
  exit 1
fi
set -a; source .env; set +a

BANK_URL="${API_URL:-http://localhost:5100}"
RTP_URL="${TCHSYSTEMS_RTP_URL:-http://localhost:8200}"
RTP_API_KEY="${Rtp__ApiKey:?Rtp__ApiKey not set in .env}"
RTP_BANK_CODE="${Rtp__BankCode:-baguette-bank}"

ok=0; fail=0; total=0

check() {
  local desc="$1" got="$2" want="$3"
  ((total++))
  if echo "$got" | grep -qi "$want"; then
    printf "  \033[32mPASS\033[0m  %s\n" "$desc"; ((ok++))
  else
    printf "  \033[31mFAIL\033[0m  %s\n         got:  %s\n         want: %s\n" "$desc" "$got" "$want"; ((fail++))
  fi
}

echo "╔══════════════════════════════════════════════════════════╗"
echo "║  Bank ↔ TCHSystems RTP  Integration Verification        ║"
echo "╚══════════════════════════════════════════════════════════╝"
echo ""

# ── 1. Health checks ──────────────────────────────────────────────────────────
echo "▶ 1. Health checks"
BANK_HEALTH=$(curl -s -o /dev/null -w "%{http_code}" "$BANK_URL/health" 2>/dev/null || echo "000")
check "Bank API ($BANK_URL)" "$BANK_HEALTH" "200"

RTP_HEALTH=$(curl -s "$RTP_URL/" 2>/dev/null || echo "{}")
RTP_MSG=$(echo "$RTP_HEALTH" | python3 -c "import sys,json; print(json.load(sys.stdin).get('message',''))" 2>/dev/null || echo "")
check "RTP System ($RTP_URL)" "$RTP_MSG" "works"

# ── 2. Bank registration ─────────────────────────────────────────────────────
echo ""
echo "▶ 2. Bank registration in TCHSystems"
BANKS=$(curl -s "$RTP_URL/banks" 2>/dev/null || echo "{}")
HAS_BANK=$(echo "$BANKS" | python3 -c "
import sys,json
data=json.load(sys.stdin)
if '$RTP_BANK_CODE' in data:
    info=data['$RTP_BANK_CODE']
    print(f\"found (status={info.get('status','?')})\")
else:
    print('not_found')
" 2>/dev/null || echo "error")
check "Bank '$RTP_BANK_CODE' registered" "$HAS_BANK" "found"

BANK_STATUS=$(echo "$BANKS" | python3 -c "
import sys,json
data=json.load(sys.stdin)
print(data.get('$RTP_BANK_CODE',{}).get('status',''))
" 2>/dev/null || echo "")
check "Bank status" "$BANK_STATUS" "ACTIVE"

# ── 3. API key handshake ─────────────────────────────────────────────────────
echo ""
echo "▶ 3. X-Api-Key handshake"
QUEUE_HTTP=$(curl -s -o /dev/null -w "%{http_code}" "$RTP_URL/queue/incoming?limit=1" \
  -H "X-Api-Key: $RTP_API_KEY" 2>/dev/null || echo "000")
check "GET /queue/incoming with API key" "$QUEUE_HTTP" "200"

BAD_KEY_HTTP=$(curl -s -o /dev/null -w "%{http_code}" "$RTP_URL/queue/incoming?limit=1" \
  -H "X-Api-Key: invalid-key-00000" 2>/dev/null || echo "000")
check "Reject invalid API key (expect 401)" "$BAD_KEY_HTTP" "401"

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
