#!/bin/sh
# Stops only CoopQA processes (test game copies, broker, runner).
for pat in "qa/P[12]/Graveyard Keeper.x86_64" "tools/CoopQA/broker.py" "tools/CoopQA/run_qa.py"; do pgrep -f "$pat" | xargs -r kill -9 2>/dev/null; done
exit 0
