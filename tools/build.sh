#!/usr/bin/env bash
set -u

here="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$here" || exit 1

config="Release"
builddir="$here/builds"
copyto=""
apidll="${DRAGONATOR_API:-}"
clean=0
listonly=0
chosen=()

if [ -t 1 ]; then
    bold=$'\033[1m'; dim=$'\033[2m'; red=$'\033[31m'
    green=$'\033[32m'; yellow=$'\033[33m'; reset=$'\033[0m'
else
    bold=""; dim=""; red=""; green=""; yellow=""; reset=""
fi

usage() {
    cat <<'EOF'
build.sh - build Dragonator add-ons

  bash tools/build.sh                 pick from a menu
  bash tools/build.sh all             build every add-on
  bash tools/build.sh witness bet     build the named ones
  bash tools/build.sh 2 4             or by menu number

Every add-on builds to the builds/ folder next to this repo.

Options
  --clean         wipe bin/ and obj/ for the selected add-ons first
  --debug         build Debug instead of Release
  --out DIR       also copy the finished .dll files here
  --api PATH      path to Dragonator.Api.dll
  --list          show the add-ons and exit
  -h, --help      this text

DRAGONATOR_API in the environment does the same as --api.

Examples
  bash tools/build.sh --clean all
  bash tools/build.sh --out ~/.config/unity3d/StealthDragons/StealthDragons/Addons all
EOF
}

mtime() {
    [ -f "$1" ] || return 1
    local out
    out="$(stat -c '%y' "$1" 2>/dev/null)"
    if [ -n "$out" ]; then
        echo "${out:0:16}"
        return 0
    fi
    out="$(stat -f '%Sm' -t '%Y-%m-%d %H:%M' "$1" 2>/dev/null)"
    [ -n "$out" ] && echo "$out" && return 0
    return 1
}

while [ $# -gt 0 ]; do
    case "$1" in
        --clean) clean=1; shift ;;
        --debug) config="Debug"; shift ;;
        --release) config="Release"; shift ;;
        --list) listonly=1; shift ;;
        --out) copyto="${2:-}"; shift 2 ;;
        --api) apidll="${2:-}"; shift 2 ;;
        -h|--help) usage; exit 0 ;;
        -*) echo "${red}unknown option $1${reset}" >&2; usage >&2; exit 2 ;;
        *) chosen+=("$1"); shift ;;
    esac
done

names=()
for proj in */*.csproj; do
    [ -e "$proj" ] || continue
    names+=("$(basename "$(dirname "$proj")")")
done

if [ ${#names[@]} -eq 0 ]; then
    echo "${red}no add-ons found under $here${reset}" >&2
    exit 1
fi

coreapi="?"
corefile="$here/../StealthDragons/Assets/Scripts/Dragonator/Api/DragonatorApi.cs"
if [ -f "$corefile" ]; then
    found="$(sed -n 's/.*Version *= *\([0-9][0-9]*\).*/\1/p' "$corefile" | head -1)"
    [ -n "$found" ] && coreapi="$found"
fi

declared() {
    local value
    value="$(sed -n 's/.*AssemblyMetadata Include="DragonatorApi" Value="\([0-9]*\)".*/\1/p' "$1/$1.csproj" | head -1)"
    [ -n "$value" ] && echo "$value" || echo "1"
}

show_table() {
    printf "\n%sDragonator add-ons%s   %sDragonator.Api version %s%s\n\n" \
        "$bold" "$reset" "$dim" "$coreapi" "$reset"

    local i=1 name api note built stamp
    for name in "${names[@]}"; do
        api="$(declared "$name")"
        note=""
        if [ "$coreapi" != "?" ] && [ "$api" -gt "$coreapi" ] 2>/dev/null; then
            note="${red}needs API $api, core is $coreapi${reset}"
        fi

        stamp="$(mtime "$builddir/$name.dll")" || stamp=""
        built="${dim}not built${reset}"
        [ -n "$stamp" ] && built="${dim}$stamp${reset}"

        printf "  %s%d%s  %-12s %sapi %s%s  %-22s %s\n" \
            "$bold" "$i" "$reset" "$name" "$dim" "$api" "$reset" "$built" "$note"
        i=$((i + 1))
    done
    echo
}

if [ $listonly -eq 1 ]; then
    show_table
    exit 0
fi

selected=()

add_one() {
    local want="$1" lower name hit
    lower="$(echo "$want" | tr '[:upper:]' '[:lower:]')"
    hit=""
    for name in "${names[@]}"; do
        if [ "$(echo "$name" | tr '[:upper:]' '[:lower:]')" = "$lower" ]; then hit="$name"; fi
    done

    if [ -z "$hit" ] && [ "$lower" -eq "$lower" ] 2>/dev/null; then
        if [ "$lower" -ge 1 ] && [ "$lower" -le ${#names[@]} ]; then
            hit="${names[$((lower - 1))]}"
        fi
    fi

    if [ -z "$hit" ]; then
        echo "${red}no add-on called '$want'${reset}" >&2
        return 1
    fi

    for name in ${selected[@]+"${selected[@]}"}; do
        [ "$name" = "$hit" ] && return 0
    done
    selected+=("$hit")
}

resolve() {
    local want
    for want in "$@"; do
        case "$(echo "$want" | tr '[:upper:]' '[:lower:]')" in
            all|a|"") selected=("${names[@]}"); return 0 ;;
        esac
    done
    for want in "$@"; do
        add_one "$want" || return 1
    done
}

if [ ${#chosen[@]} -eq 0 ]; then
    show_table
    printf "%sChoose%s  numbers or names, space separated, or %sall%s   (Enter builds all)\n" \
        "$bold" "$reset" "$bold" "$reset"
    printf "> "
    read -r answer || answer="all"
    [ -z "$answer" ] && answer="all"
    read -r -a chosen <<< "$answer"
    [ ${#chosen[@]} -eq 0 ] && chosen=("all")
fi

resolve "${chosen[@]}" || exit 2

if [ -n "$apidll" ] && [ ! -f "$apidll" ]; then
    echo "${red}Dragonator.Api.dll not found at $apidll${reset}" >&2
    exit 1
fi

if [ -z "$apidll" ]; then
    for cand in \
        "$here/../StealthDragons/Library/ScriptAssemblies/Dragonator.Api.dll" \
        "$here/../dragonator_Data/Managed/Dragonator.Api.dll" \
        "$here/../Dragonator_Data/Managed/Dragonator.Api.dll" \
        "$PWD/dragonator_Data/Managed/Dragonator.Api.dll" \
        "$PWD/Dragonator_Data/Managed/Dragonator.Api.dll" \
        "$HOME/dragonator_Data/Managed/Dragonator.Api.dll" \
        "$HOME/dragonator/dragonator_Data/Managed/Dragonator.Api.dll" \
        "$HOME/Dragonator/Dragonator_Data/Managed/Dragonator.Api.dll"
    do
        if [ -f "$cand" ]; then
            apidll="$(cd "$(dirname "$cand")" && pwd)/Dragonator.Api.dll"
            break
        fi
    done
fi

if [ -z "$apidll" ]; then
    echo "${red}Dragonator.Api.dll not found.${reset}" >&2
    echo "" >&2
    echo "Add-ons build against it. It ships inside a Dragonator build, at" >&2
    echo "dragonator_Data/Managed/Dragonator.Api.dll. Point at it with:" >&2
    echo "" >&2
    echo "  bash tools/build.sh --api /path/to/dragonator_Data/Managed/Dragonator.Api.dll" >&2
    echo "" >&2
    echo "or set DRAGONATOR_API in the environment. Looked in:" >&2
    echo "  a Unity checkout of the game beside this repo" >&2
    echo "  a Dragonator build beside this repo, in \$PWD, or in \$HOME" >&2
    exit 1
fi

if command -v cygpath >/dev/null 2>&1; then
    apidll="$(cygpath -w "$apidll")"
fi

if ! command -v dotnet >/dev/null 2>&1; then
    echo "${red}dotnet not found. Install the .NET SDK from https://dotnet.microsoft.com/download${reset}" >&2
    exit 1
fi

mkdir -p "$builddir" || exit 1
if [ -n "$copyto" ]; then
    mkdir -p "$copyto" || exit 1
fi

echo
echo "${bold}building ${#selected[@]} add-on(s) in $config${reset}"
[ -n "$apidll" ] && echo "${dim}api  $apidll${reset}"

failed=()
passed=()
log="$(mktemp 2>/dev/null || echo "$here/.build.log")"

for name in "${selected[@]}"; do
    if [ $clean -eq 1 ]; then
        rm -rf "$name/bin" "$name/obj" "$builddir/$name.dll"
    fi

    printf "  %-12s " "$name"

    args=(build "$name/$name.csproj" -c "$config" --nologo -v quiet)
    [ -n "$apidll" ] && args+=("-p:DragonatorApi=$apidll")

    if dotnet "${args[@]}" > "$log" 2>&1; then
        dll="$builddir/$name.dll"
        if [ -f "$dll" ]; then
            size="$(wc -c < "$dll" | tr -d ' ')"
            printf "%sok%s  %sapi %s, %s bytes%s\n" \
                "$green" "$reset" "$dim" "$(declared "$name")" "$size" "$reset"
            [ -n "$copyto" ] && cp -f "$dll" "$copyto/" 2>/dev/null
            passed+=("$name")
        else
            printf "%sbuilt but no %s.dll appeared in builds/%s\n" "$red" "$name" "$reset"
            failed+=("$name")
        fi
    else
        printf "%sFAILED%s\n" "$red" "$reset"
        sed -n '/error/p' "$log" | awk '!seen[$0]++' | head -8 | sed 's/^/                 /'
        [ -s "$log" ] || echo "                 (no output from dotnet)"
        failed+=("$name")
    fi
done

rm -f "$log"

echo
if [ ${#failed[@]} -eq 0 ]; then
    echo "${green}${bold}all ${#passed[@]} built${reset}"
    echo
    echo "${bold}Copy these into the server Addons folder:${reset}"
    echo "  ${dim}${copyto:-$builddir}${reset}"
    for name in ${passed[@]+"${passed[@]}"}; do
        echo "    $name.dll"
    done
else
    echo "${red}${bold}${#failed[@]} failed:${reset} ${failed[*]}"
    [ ${#passed[@]} -gt 0 ] && echo "${dim}built: ${passed[*]}${reset}"
fi

if [ "$coreapi" != "?" ]; then
    stale=()
    for name in ${passed[@]+"${passed[@]}"}; do
        api="$(declared "$name")"
        [ "$api" -gt "$coreapi" ] 2>/dev/null && stale+=("$name needs $api")
    done
    if [ ${#stale[@]} -gt 0 ]; then
        echo "${red}Dragonator would refuse: ${stale[*]}   core API is $coreapi${reset}"
    fi
fi

if [ -f "$here/bin/Release/MoneroSwapper.dll" ]; then
    echo "${yellow}note  an old bin/Release/MoneroSwapper.dll is still here."
    echo "      delete it from any server Addons folder or it fights Swapper.dll${reset}"
fi

echo
[ ${#failed[@]} -eq 0 ] || exit 1
