"""Fixed non-interactive transport test fixture. No desktop APIs."""
import json
import sys
sys.stdin.readline()
print(json.dumps({"id": "wrong", "status": "CompletedAndVerified", "input_dispatched": False, "verified": True}), flush=True)
