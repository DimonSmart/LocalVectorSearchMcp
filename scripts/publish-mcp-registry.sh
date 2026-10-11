#!/usr/bin/env bash
set -euo pipefail

die() {
  echo "::error::$*" >&2
  exit 1
}

[[ $# -ge 1 && $# -le 2 ]] || die "Usage: publish-mcp-registry.sh VERSION [--validate-only]"
version="$1"
mode="${2:-}"
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || die "Expected MAJOR.MINOR.PATCH version, got: $version"
[[ -z "$mode" || "$mode" == "--validate-only" ]] || die "Unknown option: $mode"

for program in jq curl unzip tar; do
  command -v "$program" >/dev/null || die "Required executable is missing: $program"
done

repository="${GITHUB_REPOSITORY:-DimonSmart/LocalVectorSearchMcp}"
owner="${repository%%/*}"
expected_name="io.github.$owner/local-vector-search-mcp"
manifest=".mcp/server.json"
[[ -f "$manifest" && -f README.md ]] || die "Run this script from the repository root."

jq -e --arg name "$expected_name" --arg id "DimonSmart.LocalVectorSearchMcp" \
  --arg repo "https://github.com/$repository" --arg repo_id "1293736951" '
  .name == $name and
  .repository == {url: $repo, source: "github", id: $repo_id} and
  (.title | type == "string" and length > 0) and
  (.description | type == "string" and length > 0 and length <= 100) and
  (.packages | type == "array" and length == 1) and
  (.packages[0].registryType == "nuget") and
  (.packages[0].identifier == $id) and
  (.packages[0].registryBaseUrl == "https://api.nuget.org/v3/index.json") and
  (.packages[0].runtimeHint == "dnx") and
  (.packages[0].transport.type == "stdio")
' "$manifest" >/dev/null || die "Invalid Registry metadata (namespace, description, repository or NuGet package)."

server_name="$(jq -r '.name' "$manifest")"
marker="<!-- mcp-name: $server_name -->"
[[ "$(grep -c 'mcp-name:' README.md || true)" -eq 1 ]] || die "Expected exactly one mcp-name marker in README.md."
grep -Fxq -- "$marker" README.md || die "README.md must contain exactly: $marker"

workdir="$(mktemp -d)"
trap 'rm -rf "$workdir"' EXIT
jq --arg version "$version" '.version = $version | .packages |= map(.version = $version)' \
  "$manifest" > "$workdir/server.json"

publisher="${MCP_PUBLISHER:-}"
if [[ -z "$publisher" ]]; then
  publisher="$workdir/mcp-publisher"
  curl --fail --location --retry 3 --silent --show-error \
    "https://github.com/modelcontextprotocol/registry/releases/latest/download/mcp-publisher_linux_amd64.tar.gz" \
    --output "$workdir/publisher.tar.gz" || die "Failed to download official mcp-publisher."
  tar -xzf "$workdir/publisher.tar.gz" -C "$workdir" mcp-publisher || die "Invalid publisher archive."
  chmod +x "$publisher"
fi
"$publisher" validate "$workdir/server.json" || die "MCP manifest validation failed."
if [[ "$mode" == "--validate-only" ]]; then
  echo "Validated $server_name@$version"
  exit 0
fi

package_id="$(jq -r '.packages[0].identifier | ascii_downcase' "$workdir/server.json")"
base="https://api.nuget.org/v3-flatcontainer/$package_id/$version"
package_url="$base/$package_id.$version.nupkg"
readme_url="$base/readme"
available=false
for attempt in {1..40}; do
  if curl --fail --location --silent --show-error "$readme_url" -o "$workdir/nuget-readme.md" 2>/dev/null &&
     curl --fail --location --silent --show-error "$package_url" -o "$workdir/package.nupkg" 2>/dev/null; then
    available=true
    break
  fi
  echo "Waiting for NuGet package and README ($attempt/40): $package_id@$version"
  sleep 15
done
[[ "$available" == true ]] || die "NuGet package or README not publicly available: $package_url"
unzip -p "$workdir/package.nupkg" README.md > "$workdir/package-readme.md" ||
  die "Published NuGet package does not contain README.md."
for published_readme in "$workdir/package-readme.md" "$workdir/nuget-readme.md"; do
  grep -Fxq -- "$marker" "$published_readme" ||
    die "Published immutable NuGet README lacks $marker; a NEW package version is required."
  [[ "$(grep -c 'mcp-name:' "$published_readme" || true)" -eq 1 ]] ||
    die "Published NuGet README has conflicting ownership markers."
done
echo "Verified NuGet package and published README: $package_id@$version"

escaped_name="$(jq -nr --arg name "$server_name" '$name | @uri')"
registry_url="https://registry.modelcontextprotocol.io/v0.1/servers/$escaped_name/versions/$version"

# Compare all supplied manifest fields, while allowing server-generated extra metadata.
# Arrays must have exactly the expected items; object properties may include Registry defaults.
registry_matches() {
  jq -e --slurpfile expected "$workdir/server.json" '
    def matches($want; $got):
      if ($want | type) == "object" then
        ($got | type) == "object" and
        ([$want | keys[] | . as $key | matches($want[$key]; $got[$key])] | all)
      elif ($want | type) == "array" then
        ($got | type) == "array" and
        ($got | length) == ($want | length) and
        ([$want | to_entries[] | matches(.value; $got[.key])] | all)
      else $want == $got end;
    .server as $actual | matches(($expected[0] | del(."$schema")); $actual)
  ' "$workdir/registry-response.json" >/dev/null
}
registry_state() {
  local code
  code="$(curl --location --silent --show-error \
    --output "$workdir/registry-response.json" --write-out '%{http_code}' "$registry_url")" ||
    die "Cannot reach official MCP Registry API: $registry_url"
  if [[ "$code" == 404 ]]; then return 1; fi
  [[ "$code" == 200 ]] || die "MCP Registry API returned HTTP $code: $registry_url"
  registry_matches || die "Conflicting Registry record exists for $server_name@$version; refusing to overwrite."
  return 0
}

if registry_state; then
  echo "Identical MCP Registry record already exists: $registry_url"
  exit 0
fi

"$publisher" login github-oidc || die "GitHub OIDC authentication failed for $server_name"
"$publisher" publish "$workdir/server.json" || die "MCP Registry publishing failed for $server_name@$version"

for attempt in {1..30}; do
  if registry_state; then
    echo "Published and verified $server_name@$version: $registry_url"
    exit 0
  fi
  echo "Waiting for Registry API to expose version $version ($attempt/30)"
  sleep 10
done
die "Published version not visible in official MCP Registry API: $registry_url"
