#!/bin/sh
set -eu

app_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
: "${PORT:?Alwaysdata must provide the site PORT}"
# PUBLIC_URL_DEFAULT
: "${Platform__PublicUrl:?Set Platform__PublicUrl to your HTTPS website URL}"
case "$Platform__PublicUrl" in
    https://*) ;;
    *) echo "Platform__PublicUrl must start with https://" >&2; exit 1 ;;
esac

export ASPNETCORE_ENVIRONMENT=Production
export Platform__DemoData=false
export Platform__UseHttpsProxy=true
export Platform__DataDirectory="${Platform__DataDirectory:-$app_directory/../../graduation-data}"
export Similarity__PythonExecutable="${Similarity__PythonExecutable:-python3}"

public_host=${Platform__PublicUrl#https://}
public_host=${public_host%%/*}
public_host=${public_host%%:*}
export AllowedHosts="${AllowedHosts:-$public_host}"

listen_ip=${IP:-${HOST:-::}}
case "$listen_ip" in
    \[*\]) ;;
    *:*) listen_ip="[$listen_ip]" ;;
esac

exec dotnet "$app_directory/GraduationPlatform.dll" \
    --contentRoot "$app_directory" \
    --environment Production \
    --urls "http://$listen_ip:$PORT"
