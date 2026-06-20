#!/usr/bin/env bash
# Quick connectivity check for the bank ↔ FedSystems ACH integration.
# Designed for the VanillaMile team — no bank API knowledge needed.
#
# All checks are read-only — no transfers are created, no state is modified.
#
# Usage:
#   bash verify-ach-integration.sh
#
# Reads credentials from .env in the same directory.
# Requires: curl, python3, sftp

set -euo pipefail
cd "$(dirname "$0")"

# ── load .env ──────────────────────────────────────────────────────────────────
if [[ ! -f .env ]]; then
  echo "ERROR: .env not found in $(pwd). Copy .env.example and fill in values."
  exit 1
fi
set -a; source .env; set +a

BANK_URL="${API_URL:-http://localhost:5100}"
ACH_URL="${FEDSYSTEMS_ACH_URL:-http://localhost:8310}"
ACH_RTN="${Ach__RoutingNumber:?Ach__RoutingNumber not set in .env}"
ACH_SFTP_HOST="${Ach__Sftp__Host:-localhost}"
ACH_SFTP_PORT="${Ach__Sftp__Port:-2221}"
ACH_SFTP_USER="${Ach__Sftp__Username:?Ach__Sftp__Username not set in .env}"
ACH_SFTP_KEY="${Ach__Sftp__PrivateKeyPath:-}"

# Resolve key path: docker-compose mounts it at /app/sftp_keys/id_rsa, but on the
# host the key lives next to this repo. Try common locations.
if [[ ! -f "$ACH_SFTP_KEY" ]]; then
  SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
  for candidate in \
    "$SCRIPT_DIR/../payment-settlement-systems/FedSystems/SFTP_Keys/$ACH_SFTP_USER/id_rsa" \
    "$SCRIPT_DIR/../payment-settlement-systems/FedSystems/SFTP_Keys/baguette-bank/id_rsa"; do
    if [[ -f "$candidate" ]]; then
      ACH_SFTP_KEY="$candidate"
      break
    fi
  done
fi

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
echo "║  Bank ↔ FedSystems ACH  Integration Verification        ║"
echo "╚══════════════════════════════════════════════════════════╝"
echo ""

# ── 1. Health checks ──────────────────────────────────────────────────────────
echo "▶ 1. Health checks"
BANK_HEALTH=$(curl -s -o /dev/null -w "%{http_code}" "$BANK_URL/health" 2>/dev/null || echo "000")
check "Bank API ($BANK_URL)" "$BANK_HEALTH" "200"

ACH_HEALTH=$(curl -s "$ACH_URL/health" 2>/dev/null || echo "{}")
ACH_STATUS=$(echo "$ACH_HEALTH" | python3 -c "import sys,json; print(json.load(sys.stdin).get('status',''))" 2>/dev/null || echo "")
check "ACH Backend ($ACH_URL)" "$ACH_STATUS" "ok"

# ── 2. Bank registration ─────────────────────────────────────────────────────
echo ""
echo "▶ 2. Bank registration in FedSystems ACH"
SFTP_USERS=$(curl -s "$ACH_URL/api/sftp-users" 2>/dev/null || echo "{}")
HAS_USER=$(echo "$SFTP_USERS" | python3 -c "
import sys,json
users=json.load(sys.stdin).get('sftp_users',[])
match=[u for u in users if u.get('username')=='$ACH_SFTP_USER']
print('found' if match else 'not_found')
" 2>/dev/null || echo "error")
check "SFTP user '$ACH_SFTP_USER' registered" "$HAS_USER" "found"

ACH_BANKS=$(curl -s "$ACH_URL/api/ach-banks" 2>/dev/null || echo "{}")
HAS_BANK=$(echo "$ACH_BANKS" | python3 -c "
import sys,json
banks=json.load(sys.stdin).get('banks',[])
match=[b for b in banks if b.get('primary_routing_transit_number')=='$ACH_RTN']
if match:
    p=match[0].get('ach_participant',False)
    print(f'found (participant={p})')
else:
    print('not_found')
" 2>/dev/null || echo "error")
check "Bank RTN $ACH_RTN in ACH participants" "$HAS_BANK" "found"

# ── 3. SFTP connectivity ─────────────────────────────────────────────────────
echo ""
echo "▶ 3. SFTP connectivity ($ACH_SFTP_HOST:$ACH_SFTP_PORT)"
if [[ ! -f "$ACH_SFTP_KEY" ]]; then
  printf "  \033[33mSKIP\033[0m  SFTP key not found at '%s'\n" "$ACH_SFTP_KEY"
  printf "         Set Ach__Sftp__PrivateKeyPath in .env or place key in FedSystems/SFTP_Keys/\n"
  total=$((total+1)); fail=$((fail+1))
else
  SFTP_OUT=$(sftp -o BatchMode=yes -o StrictHostKeyChecking=no -o ConnectTimeout=5 \
    -i "$ACH_SFTP_KEY" -P "$ACH_SFTP_PORT" \
    "$ACH_SFTP_USER@$ACH_SFTP_HOST" <<< "ls outbound/" 2>&1 || true)
  if echo "$SFTP_OUT" | grep -qE "outbound/|sftp>|^$"; then
    check "SFTP ls outbound/ (read-only)" "connected" "connected"
  else
    check "SFTP ls outbound/ (read-only)" "$SFTP_OUT" "connected"
  fi
fi

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
