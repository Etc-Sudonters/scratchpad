#!/usr/bin/env bash

function dates() {
    # dates $(date -d "-2 days" +%Y-%m-%d) $(date +%Y-%m-%d -d "+2 days") "1 day"
    # 2024-05-30
    # 2024-05-31
    # 2024-06-01
    # 2024-06-02
    # 2024-06-03
    local current=$1
    local destination=$2
    local duration=$3

    until [[ $current > $destination ]]; do
        echo "$current"
        current=$(date -I -d "${current} + ${duration}")
    done
}
