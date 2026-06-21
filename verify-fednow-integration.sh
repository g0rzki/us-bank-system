#!/usr/bin/env bash
# Quick connectivity check for the bank ↔ FedSystems FedNow integration.
# Designed for the VanillaMile team — no bank API knowledge needed.
#
# All checks are read-only — no messages are sent, no state is modified.
#
# Usage:
#   bash verify-fednow-integration.sh
#
# Reads credentials from .env in the same directory.
# Requires: curl, python3

set -euo pipefail
cd "$(dirname "$0")"

# ── load .env ──────────────────────────────────────────────────────────────────
if [[ ! -f .env ]]; then
  echo "ERROR: .env not found in $(pwd). Copy .env.example and fill in values."
  exit 1
fi
set -a; source .env; set +a

BANK_URL="${API_URL:-http://localhost:5100}"
FEDNOW_MQ_URL="${INTEGRATIONS_FEDNOW_MQ_URL:-http://localhost:8770}"
FEDNOW_CENTRAL_URL="${FEDNOW_CENTRAL_URL:-http://localhost:8514}"
BANK_RTN="${FedNow__BankRtn:-040104018}"

# MQ URL may use host.docker.internal — replace for host access
FEDNOW_MQ_URL_HOST=$(echo "$FEDNOW_MQ_URL" | sed 's|host.docker.internal|localhost|')

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
echo "║  Bank ↔ FedSystems FedNow  Integration Verification     ║"
echo "╚══════════════════════════════════════════════════════════╝"
echo ""

# ── 1. Health checks ──────────────────────────────────────────────────────────
echo "▶ 1. Health checks"
BANK_HEALTH=$(curl -s -o /dev/null -w "%{http_code}" "$BANK_URL/health" 2>/dev/null || echo "000")
check "Bank API ($BANK_URL)" "$BANK_HEALTH" "200"

MQ_HEALTH=$(curl -s "$FEDNOW_MQ_URL_HOST/health" 2>/dev/null || echo "{}")
MQ_STATUS=$(echo "$MQ_HEALTH" | python3 -c "import sys,json; print(json.load(sys.stdin).get('status',''))" 2>/dev/null || echo "")
check "FedNow MQ ($FEDNOW_MQ_URL_HOST)" "$MQ_STATUS" "healthy"

CENTRAL_HEALTH=$(curl -s "$FEDNOW_CENTRAL_URL/health" 2>/dev/null || echo "{}")
CENTRAL_STATUS=$(echo "$CENTRAL_HEALTH" | python3 -c "import sys,json; print(json.load(sys.stdin).get('status',''))" 2>/dev/null || echo "")
check "FedNow Central ($FEDNOW_CENTRAL_URL)" "$CENTRAL_STATUS" "healthy"

# ── 2. Bank registration ─────────────────────────────────────────────────────
echo ""
echo "▶ 2. Bank registration in FedNow"
BANKS=$(curl -s "$FEDNOW_CENTRAL_URL/banks" 2>/dev/null || echo "[]")
HAS_BANK=$(echo "$BANKS" | python3 -c "
import sys,json
banks=json.load(sys.stdin)
match=[b for b in banks if b.get('routing_number')=='$BANK_RTN']
if match:
    print(f\"found ({match[0].get('legal_name','')})\")
else:
    print('not_found')
" 2>/dev/null || echo "error")
check "Bank RTN $BANK_RTN in FedNow" "$HAS_BANK" "found"

# ── 3. MQ connectivity ───────────────────────────────────────────────────────
echo ""
echo "▶ 3. Message Queue connectivity"
MQ_INFO=$(curl -s "$FEDNOW_MQ_URL_HOST/bank-info" 2>/dev/null || echo "{}")
MQ_RTN=$(echo "$MQ_INFO" | python3 -c "import sys,json; print(json.load(sys.stdin).get('routing_number',''))" 2>/dev/null || echo "")
check "MQ bank-info routing number" "$MQ_RTN" "$BANK_RTN"

MQ_NAME=$(echo "$MQ_INFO" | python3 -c "import sys,json; print(json.load(sys.stdin).get('bank_name',''))" 2>/dev/null || echo "")
check "MQ bank-info name" "$MQ_NAME" "baguette"

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
