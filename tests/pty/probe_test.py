"""End-to-end test of StandaloneQueryChannel: run the NativeAOT tcat in a pty and play the terminal.

Usage: python3 -I tests/pty/probe_test.py <path to the published tcat>
Unix only (macOS, Linux). The unit tests cover the logic with a fake channel; this covers the real terminal I/O: raw mode,
the read timeout, and restoring the terminal afterwards. It caught a macOS bug (poll() on /dev/tty) the unit tests cannot see.
"""
import os, pty, select, sys, termios, time

TCAT = sys.argv[1]


def run(scenario, replies, env_extra=None, expect_requests=1, answer_delay=0.0):
    env = {"PATH": os.environ["PATH"], "TERM": "xterm-256color", "HOME": os.environ["HOME"]}
    env.update(env_extra or {})
    pid, master = pty.fork()
    if pid == 0:
        os.execve(TCAT, [TCAT, "--diagnostics"], env)
    requests, output = [], b""
    deadline = time.time() + 10
    start = time.time()
    replies = list(replies)
    lflag_during = None
    exited = False
    while time.time() < deadline:
        ready, _, _ = select.select([master], [], [], 0.05)
        if ready:
            try:
                data = os.read(master, 4096)
            except OSError:
                break
            if not data:
                break
            # The query is whatever the child wrote that ends with DA1; the diagnostics are plain text after it.
            if b"\x1b[c" in data and len(requests) < expect_requests:
                requests.append(data)
                if lflag_during is None:
                    lflag_during = termios.tcgetattr(master)[3]
                if replies:
                    time.sleep(answer_delay)
                    os.write(master, replies.pop(0))
                continue
            output += data
        try:
            done, status = os.waitpid(pid, os.WNOHANG)
        except ChildProcessError:
            done = pid
        if done:
            exited = True
            # drain
            try:
                while select.select([master], [], [], 0.1)[0]:
                    chunk = os.read(master, 4096)
                    if not chunk:
                        break
                    output += chunk
            except OSError:
                pass
            break
    elapsed = time.time() - start
    if not exited:
        os.kill(pid, 9)
        os.waitpid(pid, 0)
    after = termios.tcgetattr(master)[3]
    print(f"== {scenario}")
    print(f"   requests seen: {len(requests)}; elapsed {elapsed:.2f}s")
    if lflag_during is not None:
        print(f"   ICANON/ECHO during the query: {bool(lflag_during & termios.ICANON)}/{bool(lflag_during & termios.ECHO)};"
              f" after: {bool(after & termios.ICANON)}/{bool(after & termios.ECHO)}")
    return requests, output.decode("utf-8", "replace")


ok = True


def check(label, condition):
    global ok
    print(f"   [{'ok' if condition else 'FAIL'}] {label}")
    ok &= bool(condition)


# 1. A kitty-like terminal answers everything.
reqs, out = run("kitty-like terminal answers in one go",
                [b"\x1b_Gi=31;OK\x1b\\\x1bP>|kitty(0.32.2)\x1b\\\x1b[6;20;10t\x1b[?62;22c"])
check("the request carries the kitty query, XTVERSION, 16t and ends with DA1",
      reqs and b"\x1b_Gi=31,s=1,v=1,a=q,t=d,f=24;AAAA\x1b\\" in reqs[0] and b"\x1b[>0q" in reqs[0] and b"\x1b[16t" in reqs[0] and reqs[0].endswith(b"\x1b[c"))
check("protocol Kitty, confidence Probed, brand kitty 0.32.2", "protocol:    Kitty" in out and "confidence:  Probed" in out and "kitty 0.32.2" in out)
check("cell size 10x20px", "10x20px" in out)

# 2. xterm with Sixel: DA1 lists 4, so a second query asks for colour registers.
reqs, out = run("xterm with sixel asks for colour registers",
                [b"\x1bP>|XTerm(388)\x1b\\\x1b[?64;4;22c", b"\x1b[?1;0;1024S\x1b[?64c"], expect_requests=2)
check("a second request for XTSMGRAPHICS", len(reqs) == 2 and b"\x1b[?1;1;0S" in reqs[1])
check("protocol Sixel with 1024 registers", "protocol:    Sixel" in out and "sixel regs:  1024" in out)

# 3. A terminal that never answers: the 250 ms timeout, then the environment.
reqs, out = run("silent terminal times out and falls back", [], {"KITTY_WINDOW_ID": "1"})
check("fell back to the environment (Kitty) with the diagnostic", "protocol:    Kitty" in out and "did not answer" in out)

# 4. A slow answer arriving in two pieces, split mid-sequence.
reqs, out = run("answer split in two pieces", [b"\x1b_Gi=31;OK\x1b\\\x1bP>|kitty(0.3"], expect_requests=1)
check("incomplete first piece does not crash (times out cleanly)", "protocol:" in out)

print("ALL OK" if ok else "SOME CHECKS FAILED")
sys.exit(0 if ok else 1)
