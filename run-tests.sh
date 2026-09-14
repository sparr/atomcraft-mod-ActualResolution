#!/usr/bin/env bash
# Build this project's mods and run them against a test root private to this project.
#
#   ./run-tests.sh                 this project's tests. The everyday loop.
#   ./run-tests.sh --headful       with a display, for the tests that judge what is on screen
#   ./run-tests.sh --all           everything installed in the test root, no filter at all
#   ./run-tests.sh -- --atomtest-filter=Screen    an explicit filter wins over the default
#
# The default filter selects only this project's assemblies. The harness discovers tests by
# scanning every loaded assembly, so without it a stale mod zip left in the test root's Mods
# directory joins the run and can fail it for reasons that have nothing to do with this
# project.
set -euo pipefail
cd "$(dirname "$0")"

MOD_ID="ActualResolution"
# shellcheck disable=SC1091
. lib/harness.sh
require_harness

# This project's own tests. The harness matches --atomtest-filter as an unanchored regex over
# the assembly-qualified test name, so anchoring keeps it from also selecting a harness test
# that happens to mention the mod.
# This project's own tests: the mod's suite, and the conformance suite that names no mod.
MINE='^(ActualResolution|ActualResolutionConformance)\.'
RETIREMENT='^ActualResolution\.Test\.RetirementTests\.'

ARGS=(); ALL=0; ONLY_RETIREMENT=0; HAS_FILTER=0; HAS_SEPARATOR=0
for arg in "$@"; do
    case "$arg" in
        --all)                  ALL=1 ;;
        --retirement)           ONLY_RETIREMENT=1 ;;
        --atomtest-filter=*)    HAS_FILTER=1; ARGS+=("$arg") ;;
        --)                     HAS_SEPARATOR=1; ARGS+=("$arg") ;;
        *)                      ARGS+=("$arg") ;;
    esac
done

if [ "$HAS_FILTER" = 0 ] && [ "$ALL" = 0 ]; then
    # Everything after the harness's own -- goes to the game, and a second -- would be passed
    # along as a game argument rather than starting a new group, so append into the existing one.
    [ "$HAS_SEPARATOR" = 1 ] || ARGS+=(--)
    if [ "$ONLY_RETIREMENT" = 1 ]; then
        ARGS+=("--atomtest-filter=$RETIREMENT")
        echo "==> asking whether the game is still broken; a failure here means something can be retired"
    else
        ARGS+=("--atomtest-filter=$MINE" "--atomtest-exclude=$RETIREMENT")
        echo "==> running this project's tests; ./run-tests.sh --retirement for the other question"
    fi
fi

seed_test_root
extract_harness_assembly

./build.sh --install
exec "$HARNESS/run-tests.sh" --no-build --mod "$HARNESS_ZIP" ${ARGS[@]+"${ARGS[@]}"}
