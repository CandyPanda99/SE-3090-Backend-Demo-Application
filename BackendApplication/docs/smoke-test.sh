#!/usr/bin/env bash
# =============================================================================
#  End-to-end smoke test for the Backend Application Tasks API
# =============================================================================
#  Usage:
#      bash BackendApplication/docs/smoke-test.sh                       # local dotnet run  (:5064)
#      bash BackendApplication/docs/smoke-test.sh http://localhost:8080  # Docker Compose
#
#  Requires: curl, python3, and a running `backendapplication-postgres`
#  container (the script asserts against the DATABASE as well as against HTTP
#  status codes).
#
#  WHY ASSERT AGAINST THE DATABASE
#  ------------------------------------------------------------------------
#  A handler can return 200 OK having written nothing at all, and a unit test
#  with a mocked repository will never catch it, because a mock always "saves"
#  successfully. The `docker exec ... psql` assertions below read the row back.
#
#  NOTE: the login rate limiter (10/min) is real. Running this twice inside one
#  minute will fail the auth section - that is the limiter working, not a broken
#  test. Restart the API (or wait 60s) between runs.
# =============================================================================
BASE=${1:-http://localhost:5064}
PG=backendapplication-postgres
PASS=0; FAIL=0

chk() { # chk <label> <expected> <actual> [extra]
  if [ "$2" = "$3" ]; then echo "  PASS  $1  ($3)"; PASS=$((PASS+1));
  else echo "  FAIL  $1  expected $2 got $3   $4"; FAIL=$((FAIL+1)); fi
}

code() { curl -s -o /dev/null -w "%{http_code}" "$@"; }
body() { curl -s "$@"; }
psq()  { docker exec $PG psql -U tasksuser -d tasksdb -tAc "$1" | tr -d ' '; }

echo "=============================================="
echo " 1. DOCS + HEALTH"
echo "=============================================="
chk "GET /openapi/v1.json"   200 "$(code $BASE/openapi/v1.json)"
chk "GET /scalar"            200 "$(code -L $BASE/scalar)"
chk "GET /health/live"       200 "$(code $BASE/health/live)"
chk "GET /health/ready"      200 "$(code $BASE/health/ready)"

echo
echo "=============================================="
echo " 2. AUTH: 401 -> login -> 200"
echo "=============================================="
chk "GET /tasks anonymous -> 401" 401 "$(code $BASE/api/v1/tasks)"
chk "GET /auth/me anonymous -> 401" 401 "$(code $BASE/api/v1/auth/me)"

ADMIN=$(body -X POST "$BASE/api/v1/auth/login" -H 'Content-Type: application/json' \
        -d '{"email":"admin@tasks.local","password":"Admin#12345"}' \
        | python3 -c 'import sys,json;print(json.load(sys.stdin)["data"]["accessToken"])' 2>/dev/null)
if [ -n "$ADMIN" ]; then echo "  PASS  admin login (token ${#ADMIN} chars)"; PASS=$((PASS+1));
else echo "  FAIL  admin login"; FAIL=$((FAIL+1)); fi

LEAD=$(body -X POST "$BASE/api/v1/auth/login" -H 'Content-Type: application/json' \
       -d '{"email":"lead@tasks.local","password":"Lead#12345"}' \
       | python3 -c 'import sys,json;print(json.load(sys.stdin)["data"]["accessToken"])' 2>/dev/null)
MEMBER=$(body -X POST "$BASE/api/v1/auth/login" -H 'Content-Type: application/json' \
         -d '{"email":"member@tasks.local","password":"Member#12345"}' \
         | python3 -c 'import sys,json;print(json.load(sys.stdin)["data"]["accessToken"])' 2>/dev/null)
echo "  lead token: ${#LEAD} chars | member token: ${#MEMBER} chars"

chk "GET /auth/me with token -> 200" 200 "$(code -H "Authorization: Bearer $ADMIN" $BASE/api/v1/auth/me)"
chk "bad password -> 401"            401 "$(code -X POST "$BASE/api/v1/auth/login" -H 'Content-Type: application/json' -d '{"email":"admin@tasks.local","password":"wrong"}')"
echo "  whoami(member): $(body $BASE/api/v1/auth/me -H "Authorization: Bearer $MEMBER" | python3 -c 'import sys,json;d=json.load(sys.stdin)["data"];print("%s role=%s" % (d["email"], d["role"]))')"

echo
echo "=============================================="
echo " 3. LIST: PAGINATION, FILTERING, SORTING"
echo "=============================================="
chk "GET /api/v1/tasks -> 200" 200 "$(code $BASE/api/v1/tasks -H "Authorization: Bearer $MEMBER")"
P1=$(body "$BASE/api/v1/tasks?pageNumber=1&pageSize=3&sortBy=dueat&sortDirection=asc" -H "Authorization: Bearer $MEMBER")
echo "  page meta: $(echo "$P1" | python3 -c 'import sys,json;d=json.load(sys.stdin)["data"];print("items=%s total=%s pages=%s next=%s" % (len(d["items"]), d["totalCount"], d["totalPages"], bool(d["links"]["next"])))')"
echo "  X-Pagination header: $(curl -s -D- -o /dev/null "$BASE/api/v1/tasks?pageSize=3" -H "Authorization: Bearer $MEMBER" | grep -i '^x-pagination' | cut -c1-88)"
chk "filter status=InProgress"  200 "$(code "$BASE/api/v1/tasks?status=InProgress" -H "Authorization: Bearer $MEMBER")"
chk "filter priority=Critical"  200 "$(code "$BASE/api/v1/tasks?priority=Critical" -H "Authorization: Bearer $MEMBER")"
chk "search=pagination"         200 "$(code "$BASE/api/v1/tasks?search=pagination" -H "Authorization: Bearer $MEMBER")"
chk "bad enum -> 422"           422 "$(code "$BASE/api/v1/tasks?status=purple" -H "Authorization: Bearer $MEMBER")"
echo "  pageSize=9999 clamped to: $(body "$BASE/api/v1/tasks?pageSize=9999" -H "Authorization: Bearer $MEMBER" | python3 -c 'import sys,json;print(json.load(sys.stdin)["data"]["pageSize"])')"
echo "  overdueOnly=true returns: $(body "$BASE/api/v1/tasks?overdueOnly=true" -H "Authorization: Bearer $MEMBER" | python3 -c 'import sys,json;print(json.load(sys.stdin)["data"]["totalCount"])') task(s)"
echo "  unassignedOnly=true returns: $(body "$BASE/api/v1/tasks?unassignedOnly=true" -H "Authorization: Bearer $MEMBER" | python3 -c 'import sys,json;print(json.load(sys.stdin)["data"]["totalCount"])') task(s)"
echo "  sortBy=priority desc top: $(body "$BASE/api/v1/tasks?sortBy=priority&sortDirection=asc&pageSize=1" -H "Authorization: Bearer $MEMBER" | python3 -c 'import sys,json;i=json.load(sys.stdin)["data"]["items"][0];print("%s (%s)" % (i["reference"], i["priority"]))')"

echo
echo "=============================================="
echo " 4. AUTHORISATION (roles)"
echo "=============================================="
NEW='{"title":"Smoke test task","description":"created by the smoke test","priority":"High","estimatedHours":3.5}'
chk "member POST task -> 403" 403 "$(code -X POST $BASE/api/v1/tasks -H "Authorization: Bearer $MEMBER" -H 'Content-Type: application/json' -d "$NEW")"
CREATED=$(curl -s -D/tmp/h2.txt -X POST $BASE/api/v1/tasks -H "Authorization: Bearer $LEAD" -H 'Content-Type: application/json' -d "$NEW")
CODE=$(head -1 /tmp/h2.txt | awk '{print $2}')
chk "lead POST task -> 201"   201 "$CODE"
echo "  Location: $(grep -i '^location' /tmp/h2.txt | tr -d '\r')"
NEWID=$(echo "$CREATED" | python3 -c 'import sys,json;print(json.load(sys.stdin)["data"]["id"])' 2>/dev/null)
NEWREF=$(echo "$CREATED" | python3 -c 'import sys,json;print(json.load(sys.stdin)["data"]["reference"])' 2>/dev/null)
echo "  server-allocated reference: $NEWREF"
chk "reference persisted in DB" "$NEWREF" "$(psq "SELECT \"Reference\" FROM \"Tasks\" WHERE \"Id\"=$NEWID;")"
chk "new task starts in Todo"   "Todo" "$(psq "SELECT \"Status\" FROM \"Tasks\" WHERE \"Id\"=$NEWID;")"

echo
echo "=============================================="
echo " 5. VALIDATION"
echo "=============================================="
chk "empty title -> 422"        422 "$(code -X POST $BASE/api/v1/tasks -H "Authorization: Bearer $LEAD" -H 'Content-Type: application/json' -d '{"title":"","priority":"High"}')"
chk "past due date -> 422"      422 "$(code -X POST $BASE/api/v1/tasks -H "Authorization: Bearer $LEAD" -H 'Content-Type: application/json' -d '{"title":"Backdated task","dueAtUtc":"2020-01-01T00:00:00Z"}')"
chk "unknown assignee -> 422"   422 "$(code -X POST $BASE/api/v1/tasks -H "Authorization: Bearer $LEAD" -H 'Content-Type: application/json' -d '{"title":"Ghost assignee","assigneeId":9999}')"
chk "negative hours -> 422"     422 "$(code -X POST $BASE/api/v1/tasks -H "Authorization: Bearer $LEAD" -H 'Content-Type: application/json' -d '{"title":"Negative estimate","estimatedHours":-5}')"
chk "unknown JSON field -> 422" 422 "$(code -X POST $BASE/api/v1/tasks -H "Authorization: Bearer $LEAD" -H 'Content-Type: application/json' -d '{"title":"Valid title","isDeleted":true}')"
echo "  errors: $(body -X POST $BASE/api/v1/tasks -H "Authorization: Bearer $LEAD" -H 'Content-Type: application/json' -d '{"title":"x","estimatedHours":-5}' | python3 -c 'import sys,json;print(json.dumps(json.load(sys.stdin).get("errors"))[:220])')"

echo
echo "=============================================="
echo " 6. THE STATUS STATE MACHINE"
echo "=============================================="
patch() { local b="{\"status\":\"$3\"}"; code -X PATCH $BASE/api/v1/tasks/$1/status -H "Authorization: Bearer $2" -H 'Content-Type: application/json' -d "$b"; }
echo "  allowedNextStatuses for a Todo task: $(body $BASE/api/v1/tasks/$NEWID -H "Authorization: Bearer $LEAD" | python3 -c 'import sys,json;print(json.load(sys.stdin)["data"]["allowedNextStatuses"])')"
chk "Todo -> Done (illegal) -> 409"        409 "$(patch $NEWID "$LEAD" Done)"
chk "Todo -> InProgress, unassigned -> 400" 400 "$(patch $NEWID "$LEAD" InProgress)"
# Assign it, then start it.
MEMBERID=$(body $BASE/api/v1/auth/me -H "Authorization: Bearer $MEMBER" | python3 -c 'import sys,json;print(json.load(sys.stdin)["data"]["id"])')
# Build the body in a variable first. Backslash-escaped JSON written inline inside a
# nested "$( ... )" gets mangled by the shell before curl ever sees it, and the API then
# answers 422 for a body the script believes it sent correctly.
ASSIGN_BODY="{\"title\":\"Smoke test task\",\"priority\":\"High\",\"estimatedHours\":3.5,\"assigneeId\":$MEMBERID}"
chk "assign via PUT -> 200" 200 "$(code -X PUT $BASE/api/v1/tasks/$NEWID -H "Authorization: Bearer $LEAD" -H 'Content-Type: application/json' -d "$ASSIGN_BODY")"
chk "Todo -> InProgress -> 200"            200 "$(patch $NEWID "$LEAD" InProgress)"
chk "status PERSISTED as InProgress" "InProgress" "$(psq "SELECT \"Status\" FROM \"Tasks\" WHERE \"Id\"=$NEWID;")"
chk "InProgress -> Blocked -> 200"         200 "$(patch $NEWID "$LEAD" Blocked)"
chk "Blocked -> Done (illegal) -> 409"     409 "$(patch $NEWID "$LEAD" Done)"
chk "Blocked -> InProgress -> 200"         200 "$(patch $NEWID "$LEAD" InProgress)"
chk "InProgress -> Done -> 200"            200 "$(patch $NEWID "$LEAD" Done)"
chk "CompletedAtUtc stamped in DB" "1" "$(psq "SELECT COUNT(*) FROM \"Tasks\" WHERE \"Id\"=$NEWID AND \"CompletedAtUtc\" IS NOT NULL;")"
chk "Done -> InProgress (terminal) -> 409" 409 "$(patch $NEWID "$LEAD" InProgress)"
chk "editing a closed task -> 400"         400 "$(code -X PUT $BASE/api/v1/tasks/$NEWID -H "Authorization: Bearer $LEAD" -H 'Content-Type: application/json' -d '{"title":"Reopened","priority":"Low"}')"
echo "  409 body:"
body -X PATCH $BASE/api/v1/tasks/$NEWID/status -H "Authorization: Bearer $LEAD" -H 'Content-Type: application/json' -d '{"status":"InProgress"}' | python3 -m json.tool | sed 's/^/    /'

echo
echo "=============================================="
echo " 7. OWNERSHIP RULE (the IDOR check)"
echo "=============================================="
# TSK-0001 is seeded assigned to the LEAD, so a member must not be able to edit it.
OTHER=$(psq "SELECT \"Id\" FROM \"Tasks\" WHERE \"Reference\"='TSK-0001';")
chk "member edits someone else's task -> 403" 403 "$(code -X PUT $BASE/api/v1/tasks/$OTHER -H "Authorization: Bearer $MEMBER" -H 'Content-Type: application/json' -d '{"title":"Hijacked","priority":"Low"}')"
chk "member patches someone else's task -> 403" 403 "$(patch $OTHER "$MEMBER" Cancelled)"
chk "lead edits any task -> 200" 200 "$(code -X PUT $BASE/api/v1/tasks/$OTHER -H "Authorization: Bearer $LEAD" -H 'Content-Type: application/json' -d '{"title":"Write the project charter","priority":"High"}')"

echo
echo "=============================================="
echo " 8. ERROR HANDLING + SOFT DELETE"
echo "=============================================="
chk "GET missing task -> 404"   404 "$(code $BASE/api/v1/tasks/999999 -H "Authorization: Bearer $MEMBER")"
echo "  404 body:"
body "$BASE/api/v1/tasks/999999" -H "Authorization: Bearer $MEMBER" | python3 -m json.tool | sed 's/^/    /'
echo "  content-type: $(curl -s -D- -o /dev/null "$BASE/api/v1/tasks/999999" -H "Authorization: Bearer $MEMBER" | grep -i '^content-type')"
chk "lead DELETE -> 403 (admin only)" 403 "$(code -X DELETE $BASE/api/v1/tasks/$NEWID -H "Authorization: Bearer $LEAD")"
chk "admin DELETE -> 204"             204 "$(code -X DELETE $BASE/api/v1/tasks/$NEWID -H "Authorization: Bearer $ADMIN")"
chk "deleted task now 404"            404 "$(code $BASE/api/v1/tasks/$NEWID -H "Authorization: Bearer $MEMBER")"
echo "  row still in DB: $(psq "SELECT \"Reference\"||' IsDeleted='||\"IsDeleted\" FROM \"Tasks\" WHERE \"Id\"=$NEWID;")"
chk "reference NOT reused after delete" "0" "$(psq "SELECT COUNT(*) FROM \"Tasks\" WHERE \"Reference\"='$NEWREF' AND \"IsDeleted\"=false;")"

echo
echo "=============================================="
echo " 9. AUDIT COLUMNS + CORRELATION + RATE LIMITING"
echo "=============================================="
echo "  audit row: $(psq "SELECT \"CreatedBy\"||' | '||COALESCE(\"UpdatedBy\",'-') FROM \"Tasks\" WHERE \"Id\"=$NEWID;")"
echo "  correlation id echoed: $(curl -s -D- -o /dev/null -H 'X-Correlation-ID: LECTURE-DEMO-42' -H "Authorization: Bearer $MEMBER" $BASE/api/v1/tasks | grep -i '^x-correlation-id')"
echo -n "  15 rapid logins -> codes: "
for i in $(seq 1 15); do printf "%s " "$(code -X POST $BASE/api/v1/auth/login -H 'Content-Type: application/json' -d '{"email":"nobody@x.com","password":"zzz"}')"; done
echo

echo
echo "=============================================="
printf " RESULT: %d passed, %d failed\n" $PASS $FAIL
echo "=============================================="
[ $FAIL -eq 0 ]
