#!/usr/bin/env bash
set -euo pipefail

echo "== OS =="
cat /etc/os-release
echo "== CPU / memory / disk =="
nproc
free -h
df -hT
echo "== Listening ports =="
ss -lntup
echo "== Firewall =="
command -v ufw >/dev/null && ufw status verbose || echo "ufw not installed"
echo "== SSH effective security settings =="
sshd -T 2>/dev/null | grep -E '^(permitrootlogin|passwordauthentication|pubkeyauthentication|maxauthtries|allowusers) ' || true
echo "== Docker =="
docker version 2>/dev/null || echo "docker unavailable"
docker info 2>/dev/null | grep -E 'Server Version|Docker Root Dir|Logging Driver|Cgroup' || true
echo "== Updates =="
apt-get -s upgrade 2>/dev/null | grep '^Inst ' || true
echo "== Users with shells =="
awk -F: '$7 !~ /(nologin|false)$/ {print $1, $6, $7}' /etc/passwd
echo "== Time sync =="
timedatectl status || true
