"""Fixed non-interactive transport test fixture. No desktop APIs."""
import os
import sys
sys.stdin.readline()
os._exit(3)
