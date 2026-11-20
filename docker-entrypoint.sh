#!/bin/sh
set -eu

if [ "$(id -u)" -eq 0 ]; then
    if [ "${DataProtection__KeyPath:-}" = /data/keys ]; then
        key_dir=/data/keys
        app_gid=$(id -g app)
        if [ -L "$key_dir" ]; then
            echo "The persistent key directory must not be a symbolic link." >&2
            exit 1
        fi
        install -d -m 0700 -o "$APP_UID" -g "$app_gid" "$key_dir"
    fi

    exec setpriv --reuid "$APP_UID" --regid "$(id -g app)" --init-groups --no-new-privs \
        dotnet Digital_Pharma.dll "$@"
fi

exec dotnet Digital_Pharma.dll "$@"
