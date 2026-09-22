#!/usr/bin/env bash
# Optional non-container fixture for a disposable Linux/WSL development machine.
set -euo pipefail
fixture=/var/tmp/wiseshell-cpp-fixture
account=wiseshellcpp_test
case "${1:-}" in
  start)
    if id "$account" >/dev/null 2>&1 || test -e "$fixture"; then
      echo 'Fixture already exists; refuse to modify an existing account/directory.' >&2
      exit 1
    fi
    mkdir -m 755 "$fixture"
    useradd -m -d "$fixture/home" -s /bin/bash "$account"
    printf '%s:%s\n' "$account" 'local-test-only' | chpasswd
    ssh-keygen -q -t ed25519 -N '' -f "$fixture/hostkey"
    ssh-keygen -q -t ed25519 -N 'test-key-passphrase' -f "$fixture/clientkey"
    install -d -m 700 -o "$account" -g "$account" "$fixture/home/.ssh"
    install -m 600 -o "$account" -g "$account" "$fixture/clientkey.pub" "$fixture/home/.ssh/authorized_keys"
    mkdir -p /run/sshd
    cat > "$fixture/sshd_config" <<EOF
ListenAddress 127.0.0.1
Port 22222
HostKey $fixture/hostkey
PidFile $fixture/sshd.pid
PasswordAuthentication yes
KbdInteractiveAuthentication no
PermitRootLogin no
UsePAM no
AllowUsers $account
Subsystem sftp internal-sftp
EOF
    /usr/sbin/sshd -f "$fixture/sshd_config" -E "$fixture/sshd.log"
    ;;
  stop)
    if test -f "$fixture/sshd.pid"; then
      pid=$(cat "$fixture/sshd.pid")
      if test -r "/proc/$pid/cmdline" && tr '\0' ' ' < "/proc/$pid/cmdline" | grep -q "$fixture/sshd_config"; then
        kill "$pid"
      fi
    fi
    # Only remove the test account if its home belongs to this exact fixture.
    if getent passwd "$account" | cut -d: -f6 | grep -qx "$fixture/home"; then
      userdel -r "$account"
    fi
    if test "$(realpath -m "$fixture")" = /var/tmp/wiseshell-cpp-fixture; then
      rm -rf -- "$fixture"
    fi
    ;;
  *) echo 'Usage: sudo bash local-fixture.sh start|stop' >&2; exit 2 ;;
esac
