#!/usr/bin/env bash
# Packs the game as one self-contained Godot project zip, for people who just want to open it in
# Godot: Project Manager → Import → pick the zip. Godot's import unpacks only the folder holding
# project.godot, so the repo's layout (game/ beside src/, solution at the root) won't do. In the zip,
# the simulation sits inside the project:
#   Pb/project.godot        solution in the project folder (dotnet/project/solution_directory removed)
#   Pb/Pb.csproj            references Pb.Sim/Pb.Sim.csproj and doesn't compile its sources itself
#   Pb/Pb.sln               the game and the sim, with Godot's Export configurations
#   Pb/Pb.Sim/              src/Pb.Sim, hidden from Godot's importer by a .gdignore
#   Pb/Directory.Build.props, Pb/global.json, Pb/HOW-TO-PLAY.txt
# Only files tracked by git go in.
#   tools/package/godot-project.sh [out-dir]    (default: builds/)
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
out="$(mkdir -p "${1:-$root/builds}" && cd "${1:-$root/builds}" && pwd)"
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT
project="$stage/Pb"

cd "$root"
git ls-files -z -- game src/Pb.Sim | tar --null -T - -cf - | tar -xf - -C "$stage"
mv "$stage/game" "$project"
mv "$stage/src/Pb.Sim" "$project/Pb.Sim"
touch "$project/Pb.Sim/.gdignore"
cp Directory.Build.props global.json "$project/"

sed -i '/^project\/solution_directory=/d' "$project/project.godot"
sed -i 's|<ProjectReference Include="..\\src\\Pb.Sim\\Pb.Sim.csproj" />|<ProjectReference Include="Pb.Sim\\Pb.Sim.csproj" />\n    <Compile Remove="Pb.Sim\\**" />|' "$project/Pb.csproj"
sed -i 's|Gameplay rules live in src/Pb.Sim.|Gameplay rules live in Pb.Sim/.|' "$project/Pb.csproj"
grep -q 'Include="Pb.Sim\\Pb.Sim.csproj"' "$project/Pb.csproj" || { echo "Pb.csproj: the sim reference wasn't rewritten" >&2; exit 1; }

sim="{9A400EE2-D34C-4432-B455-B8DA3D7232F0}"
game="{267A4332-17E6-44FB-9BBA-12399CBEF23C}"
{
  printf '\xef\xbb\xbf\r\n'
  printf 'Microsoft Visual Studio Solution File, Format Version 12.00\r\n'
  printf '# Visual Studio Version 17\r\n'
  printf 'Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Pb.Sim", "Pb.Sim\\Pb.Sim.csproj", "%s"\r\nEndProject\r\n' "$sim"
  printf 'Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Pb", "Pb.csproj", "%s"\r\nEndProject\r\n' "$game"
  printf 'Global\r\n\tGlobalSection(SolutionConfigurationPlatforms) = preSolution\r\n'
  for c in Debug Release ExportDebug ExportRelease; do printf '\t\t%s|Any CPU = %s|Any CPU\r\n' "$c" "$c"; done
  printf '\tEndGlobalSection\r\n\tGlobalSection(ProjectConfigurationPlatforms) = postSolution\r\n'
  # The sim builds as Debug or Release; the game takes Godot's own configurations, as in Pb.sln.
  for pair in Debug:Debug Release:Release ExportDebug:Debug ExportRelease:Release; do
    printf '\t\t%s.%s|Any CPU.ActiveCfg = %s|Any CPU\r\n\t\t%s.%s|Any CPU.Build.0 = %s|Any CPU\r\n' "$sim" "${pair%%:*}" "${pair#*:}" "$sim" "${pair%%:*}" "${pair#*:}"
  done
  for pair in Debug:Debug Release:ExportRelease ExportDebug:ExportDebug ExportRelease:ExportRelease; do
    printf '\t\t%s.%s|Any CPU.ActiveCfg = %s|Any CPU\r\n\t\t%s.%s|Any CPU.Build.0 = %s|Any CPU\r\n' "$game" "${pair%%:*}" "${pair#*:}" "$game" "${pair%%:*}" "${pair#*:}"
  done
  printf '\tEndGlobalSection\r\nEndGlobal\r\n'
} > "$project/Pb.sln"

cat > "$project/HOW-TO-PLAY.txt" <<'EOF'
Pb: how to open and play

You need Godot 4.7.2 (the .NET download, not the standard one) and the .NET 8 SDK (or newer).

1. In Godot's Project Manager, click Import and choose this zip (or project.godot in this
   folder if you've unzipped it). Pick an empty folder to install it into, then Import & Edit.
2. The first open takes a few minutes: Godot imports the art and builds the C# code. If it
   asks you to build, click the hammer (Build) button at the top right.
3. Press F5 for the main menu: Play, then Oxbarrow Works, a difficulty, and Start.

Controls: WASD and mouse, left click fires, Shift sprints, Ctrl or C crouches, Space jumps,
Q / E lean, X swaps shoulder, V slides, R refills the loader, Esc pauses.
F4 shows the frame rate, F12 switches graphics presets, F3 shows what the bots are thinking.
EOF

commit="$(git rev-parse --short HEAD)"
zip_path="$out/Pb-godot-project.zip"
rm -f "$zip_path"
(cd "$stage" && zip -qr -X "$zip_path" Pb)
echo "Godot project zip ($commit): $zip_path ($(du -h "$zip_path" | cut -f1))"
