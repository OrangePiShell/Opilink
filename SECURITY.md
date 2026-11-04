# Security Policy

## Supported versions
Only the latest release receives fixes.

## Reporting a vulnerability
Please **do not** open a public issue for security problems.
Use GitHub's private reporting: **Security → Report a vulnerability** on this repository.
If that is not available, open a normal issue that only asks for a private contact (do not put any details in it).

Include what you found, how to reproduce it, and the version. You can expect a first answer within about 7 days.

## What the app does with credentials
- It never reads, sends or stores your board password. The password is typed into a normal `ssh` window.
- It creates its own key `%USERPROFILE%\.ssh\opilink_ed25519` and copies the public part to the board's `authorized_keys`. Anyone who gets the private key file can log in to boards that trust it, so protect it like any SSH key.
