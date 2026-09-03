#!/usr/bin/env bash
# Recreates what building the Avalonia fork needs on Fedora, and prints the
# environment to use. See docs/LunaDE.md §7 for why each piece is required -
# none of it is discoverable from the error messages.
#
# Source it:   . tools/fedora-build-env.sh
# then:        $DOTNET build src/Avalonia.Wayland/Avalonia.Wayland.csproj
#
# The OpenSSL config lands in a real directory rather than /tmp, because the
# /tmp copies used on 2026-08-27 were gone by the next session and the recipe
# had to be reconstructed from the man page.
set -u

CONF_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/lunade"
mkdir -p "$CONF_DIR"

# .NET strong-name signing uses SHA-1; the digest is fixed by the format, not
# chosen. Fedora's crypto policy refuses it, so Avalonia's Cecil-based XAML
# compiler dies with "invalid digest". Turning signing off is NOT an option -
# InternalsVisibleTo is keyed on strong names and removing them removes the
# internals (298 errors). This override is scoped to the build process via
# OPENSSL_CONF and changes nothing system-wide. Do not reach for
# `update-crypto-policies --set LEGACY` instead: that weakens every TLS
# decision on the machine, permanently, to fix one build.
sed 's/^rh-allow-sha1-signatures.*/rh-allow-sha1-signatures = yes/' \
    /etc/crypto-policies/back-ends/opensslcnf.config > "$CONF_DIR/opensslcnf-sha1.config"

sed "s#^\.include = /etc/crypto-policies/back-ends/opensslcnf.config#.include = $CONF_DIR/opensslcnf-sha1.config#" \
    /etc/pki/tls/openssl.cnf > "$CONF_DIR/openssl-sha1.cnf"

export OPENSSL_CONF="$CONF_DIR/openssl-sha1.cnf"

# Fedora ships 10.0.111 and cannot go higher: Microsoft's Fedora repositories
# are empty stubs and they publish no rpm for .NET at all. The tarball install
# is the supported path, and global.json's `rollForward: latestFeature` makes
# it satisfy the 10.0.201 pin with no edit to that file.
if [ -x "$HOME/.dotnet/dotnet" ]; then
    export DOTNET="$HOME/.dotnet/dotnet"
else
    echo "warning: ~/.dotnet/dotnet not found. Install with:" >&2
    echo "  curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0" >&2
    export DOTNET="dotnet"
fi

echo "OPENSSL_CONF=$OPENSSL_CONF"
echo "DOTNET=$DOTNET  ($($DOTNET --version 2>/dev/null || echo 'not runnable'))"
echo
echo "Avalonia fork: ~/Projects/Avalonia, branch lunade/layer-shell"
echo "Build:  \$DOTNET build src/Avalonia.Wayland/Avalonia.Wayland.csproj"
echo "Submodules must be initialised: git submodule update --init --recursive"
