#!/bin/sh
set -eu

readonly dockerfile=Dockerfile
readonly driver_stage=driver-build
readonly app_stage=app-build
readonly both_sides="Dockerfile .dockerignore Directory.Build.props Directory.Packages.props docker/entrypoint.sh"
readonly app_side_only="patches docker/fonts.conf docker/notices LICENSE THIRD-PARTY-NOTICES.md"
readonly digits=12

fail() {
  echo "$*" >&2
  exit 1
}

published_by() {
  awk -v wanted="$1" '
    toupper($1) == "FROM" { stage = ""; for (i = 2; i < NF; i++) if (toupper($i) == "AS") stage = $(i + 1) }
    stage == wanted { for (i = 1; i < NF; i++) if ($i == "publish" && $(i + 1) ~ /\.csproj$/) print $(i + 1) }
  ' "${dockerfile}"
}

published_elsewhere() {
  awk -v driver="${driver_stage}" -v app="${app_stage}" '
    toupper($1) == "FROM" { stage = ""; for (i = 2; i < NF; i++) if (toupper($i) == "AS") stage = $(i + 1) }
    stage != driver && stage != app { for (i = 1; i < NF; i++) if ($i == "publish" && $(i + 1) ~ /\.csproj$/) print stage ": " $(i + 1) }
  ' "${dockerfile}"
}

copied_from_the_context() {
  awk '
    toupper($1) == "COPY" {
      for (i = 2; i <= NF; i++) if ($i ~ /^--from=/) next
      for (i = 2; i < NF; i++) if ($i !~ /^--/) print $i
    }
  ' "${dockerfile}" | sed 's:/*$::' | LC_ALL=C sort -u
}

referenced_by() {
  sed -n 's/.*<ProjectReference[^>]*Include="\([^"]*\)".*/\1/p' "$1" | tr '\\' '/'
}

closure_of() {
  pending="$*"
  seen=""

  while [ -n "${pending}" ]; do
    project="${pending%% *}"

    if [ "${pending}" = "${project}" ]; then
      pending=""
    else
      pending="${pending#* }"
    fi

    case " ${seen} " in
      *" ${project} "*) continue ;;
    esac

    [ -f "${project}" ] || fail "${project} is published or referenced but is not on disk, so the side it belongs to cannot be derived"
    seen="${seen:+${seen} }${project}"

    for reference in $(referenced_by "${project}"); do
      pending="${pending:+${pending} }$(realpath -m --relative-to=. "$(dirname "${project}")/${reference}")"
    done
  done

  echo "${seen}" | tr ' ' '\n'
}

directories_of() {
  for project in "$@"; do
    dirname "${project}"
  done
}

inputs_of() {
  case "$1" in
    driver)
      directories_of ${driver_closure}
      echo "${both_sides}" | tr ' ' '\n'
      ;;
    app)
      directories_of ${app_closure}
      echo "${both_sides} ${app_side_only}" | tr ' ' '\n'
      ;;
    *) fail "there is no side called '$1': the sides are driver and app" ;;
  esac | LC_ALL=C sort -u
}

within_a_side() {
  for input in ${every_input}; do
    case "$1" in
      "${input}" | "${input}"/*) return 0 ;;
    esac
  done

  return 1
}

derive() {
  [ "$(git rev-parse --is-shallow-repository)" = false ] \
    || fail "this clone is shallow, so every path would look last touched by the newest commit and both tags would move on every push"

  strays="$(published_elsewhere)"
  [ -z "${strays}" ] \
    || fail "a stage other than ${driver_stage} and ${app_stage} publishes a project, and no side has been decided for it: ${strays}"

  driver_roots="$(published_by "${driver_stage}")"
  app_roots="$(published_by "${app_stage}")"
  [ -n "${driver_roots}" ] || fail "the ${driver_stage} stage of the ${dockerfile} publishes no project, so the driver side cannot be derived"
  [ -n "${app_roots}" ] || fail "the ${app_stage} stage of the ${dockerfile} publishes no project, so the app side cannot be derived"

  driver_closure="$(closure_of ${driver_roots})"
  app_closure="$(closure_of ${app_roots})"

  for root in ${app_roots}; do
    case " $(echo "${driver_closure}" | tr '\n' ' ') " in
      *" ${root} "*) fail "the driver side reaches ${root}, which the app side publishes, so every app change would move the driver tag" ;;
    esac
  done

  for root in ${driver_roots}; do
    case " $(echo "${app_closure}" | tr '\n' ' ') " in
      *" ${root} "*) fail "the app side reaches ${root}, which the driver side publishes, so every driver change would move the app tag" ;;
    esac
  done

  for project in ${driver_closure} ${app_closure}; do
    outside="$(grep -nE '(Include|Project|Link)="[^"]*\.\.' "${project}" | grep -v '<ProjectReference' || true)"
    [ -z "${outside}" ] \
      || fail "${project} takes in a file from outside its own directory, which the tags would not see change: ${outside}"
  done

  every_input="$( (inputs_of driver; inputs_of app) | LC_ALL=C sort -u)"

  for input in ${every_input}; do
    [ -n "$(git ls-files -- "${input}")" ] || fail "${input} is named as part of a side but nothing is tracked there"
  done

  for source in src $(copied_from_the_context); do
    files="$(git ls-files -- "${source}")"
    [ -n "${files}" ] || fail "the ${dockerfile} copies ${source} but nothing is tracked there"

    for file in ${files}; do
      within_a_side "${file}" \
        || fail "${file} goes into the image but belongs to neither side, so changing it would move no tag"
    done
  done
}

last_touched() {
  commit="$(inputs_of "$1" | xargs git log -1 --format=%H --)"
  [ -n "${commit}" ] || fail "no commit in this history touches the $1 side"
  echo "${commit}" | cut -c "1-${digits}"
}

tags() {
  derive
  driver_commit="$(last_touched driver)"
  app_commit="$(last_touched app)"
  echo "driver=driver-sha-${driver_commit}"
  echo "app=app-sha-${app_commit}"
}

tag_in() {
  (cd "$1" && tags) | sed -n "s/^$2=//p"
}

a_file_of() {
  only_in="$(inputs_of "$1" | LC_ALL=C comm -23 - "$2" | grep '^src/' || true)"
  [ -n "${only_in}" ] || fail "no project belongs to the $1 side alone, so there is no change to try that should move one tag and not the other"
  echo "${only_in}" | xargs git ls-files -- | sed -n 1p
}

prove() {
  derive

  held="$(mktemp -d)"
  trap 'rm -rf "${held}"' EXIT

  scratch="${held}/scratch"
  git clone --quiet --shared --no-checkout "$(git rev-parse --path-format=absolute --git-common-dir)" "${scratch}"
  head="$(git rev-parse HEAD)"
  git -C "${scratch}" checkout --quiet --detach "${head}"

  driver_before="$(tag_in "${scratch}" driver)"
  app_before="$(tag_in "${scratch}" app)"
  echo "at ${head}: ${driver_before} and ${app_before}"

  inputs_of driver > "${held}/driver"
  inputs_of app > "${held}/app"

  app_file="$(a_file_of app "${held}/driver")"
  driver_file="$(a_file_of driver "${held}/app")"
  shared="$(LC_ALL=C comm -12 "${held}/driver" "${held}/app" | grep '^src/' || true)"
  [ -n "${shared}" ] || fail "no project belongs to both sides, so there is no change to try that should move both tags"
  shared_file="$(echo "${shared}" | xargs git ls-files -- | sed -n 1p)"
  outside_file="$(git ls-files | while read -r file; do within_a_side "${file}" || { echo "${file}"; break; }; done)"

  [ -n "${outside_file}" ] || fail "no tracked file lies outside both sides, so there is no change to try that should move neither tag"

  changed() {
    git -C "${scratch}" reset --quiet --hard "${head}"
    printf '\n' >> "${scratch}/$1"
    git -C "${scratch}" -c user.name=image-tags -c user.email=image-tags@invalid -c commit.gpgsign=false \
      commit --quiet --all --message "a change to $1"
    made="$(git -C "${scratch}" rev-parse HEAD | cut -c "1-${digits}")"
    driver_after="$(tag_in "${scratch}" driver)"
    app_after="$(tag_in "${scratch}" app)"
  }

  changed "${app_file}"
  [ "${driver_after}" = "${driver_before}" ] || fail "a change to ${app_file} alone moved the driver tag to ${driver_after}"
  [ "${app_after}" = "app-sha-${made}" ] || fail "a change to ${app_file} left the app tag at ${app_after}"
  echo "after ${app_file}: the driver tag stays, the app tag moves to ${app_after}"

  changed "${driver_file}"
  [ "${driver_after}" = "driver-sha-${made}" ] || fail "a change to ${driver_file} left the driver tag at ${driver_after}"
  [ "${app_after}" = "${app_before}" ] || fail "a change to ${driver_file} alone moved the app tag to ${app_after}"
  echo "after ${driver_file}: the driver tag moves to ${driver_after}, the app tag stays"

  changed "${shared_file}"
  [ "${driver_after}" = "driver-sha-${made}" ] || fail "a change to ${shared_file} left the driver tag at ${driver_after}"
  [ "${app_after}" = "app-sha-${made}" ] || fail "a change to ${shared_file} left the app tag at ${app_after}"
  echo "after ${shared_file}: both tags move, to ${driver_after} and ${app_after}"

  changed "${outside_file}"
  [ "${driver_after}" = "${driver_before}" ] || fail "a change to ${outside_file}, which is in neither side, moved the driver tag"
  [ "${app_after}" = "${app_before}" ] || fail "a change to ${outside_file}, which is in neither side, moved the app tag"
  echo "after ${outside_file}: both tags stay"

  git -C "${scratch}" reset --quiet --hard "${head}"
  git -c advice.detachedHead=false clone --quiet --depth 1 "file://${scratch}" "${held}/shallow"

  if refusal="$( (cd "${held}/shallow" && tags) 2>&1)"; then
    fail "a shallow clone was given tags, though every path in it looks last touched by its one commit: ${refusal}"
  fi

  case "${refusal}" in
    *shallow*) echo "a shallow clone is refused" ;;
    *) fail "a shallow clone was refused, but not for being shallow: ${refusal}" ;;
  esac
}

cd "$(git rev-parse --show-toplevel)"

case "${1:-}" in
  tags) tags ;;
  inputs)
    derive
    inputs_of "${2:?the side to list the inputs of: driver or app}"
    ;;
  prove) prove ;;
  *) fail "usage: $0 tags | inputs <driver|app> | prove" ;;
esac
