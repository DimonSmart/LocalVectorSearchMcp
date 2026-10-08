#!/usr/bin/env bash
set -euo pipefail

# Publish metadata for an already published, immutable NuGet version.
version="${1:?Usage: publish-mcp-registry.sh VERSION [--validate-only]}"
mode="${2:-}"
if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Invalid release version: $version" >&2
  exit 1
fi

workdir="$(mktemp -d)"
trap 'rm -rf "$workdir"' EXIT
jq --arg version "$version" \
  '.version = $version | .packages |= map(.version = $version)' \
  .mcp/server.json > "$workdir/server.json"

publisher="${MCP_PUBLISHER:-}"
if [[ -z "$publisher" ]]; then
  publisher="$workdir/mcp-publisher"
  url="https://github.com/modelcontextprotocol/registry/releases/latest/download/mcp-publisher_linux_amd64.tar.gz"
  curl --fail --location --retry 3 --silent --show-error "$url" \
    --output "$workdir/publisher.tar.gz"
  tar -xzf "$workdir/publisher.tar.gz" -C "$workdir" mcp-publisher
  chmod +x "$publisher"
fi

"$publisher" validate "$workdir/server.json"
if [[ "$mode" == "--validate-only" ]]; then
  exit 0
fi
if [[ -n "$mode" ]]; then
  echo "Unknown option: $mode" >&2
  exit 1
fi

package_url="https://api.nuget.org/v3-flatcontainer/dimonsmart.localvectorsearchmcp/$version/dimonsmart.localvectorsearchmcp.$version.nupkg"
found=false
for attempt in {1..40}; do
  if curl --fail --location --silent --show-error --head "$package_url" >/dev/null 2>&1; then
    found=true
    break
  fi
  sleep 15
done
if [[ "$found" != true ]]; then
  echo "NuGet package is not publicly available: $package_url" >&2
  exit 1
fi

registry_url="https://registry.modelcontextprotocol.io/v0.1/servers/io.github.dimonsmart%2Flocal-vector-search-mcp/versions/$version"
verify_existing() {
  curl --fail --silent --show-error "$registry_url" |
    jq -e --arg version "$version" \
      '.server.version == $version and
       (.server.packages | any(.identifier == "DimonSmart.LocalVectorSearchMcp" and .version == $version))' >/dev/null
}

# Existing identical metadata are safe to reuse on manual retry.
if verify_existing 2>/dev/null; then
  echo "MCP Registry already contains the expected package version $version."
  exit 0
fi

"$publisher" login github-oidc
"$publisher" publish "$workdir/server.json"

for attempt in {1..30}; do
  if verify_existing 2>/dev/null; then
    echo "MCP Registry publication verified: $registry_url"
    exit 0
  fi
  sleep 10
done
echo "MCP Registry did not return the published record: $registry_url" >&2
exit 1
