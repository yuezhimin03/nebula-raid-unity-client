# Security notes

Please do not treat the sample hot-update pipeline as a production trust system.
It validates paths, sizes and SHA-256 integrity, but the manifest is not signed and
the repository bundles no Lua sandbox or VM.

For production use, add publisher signatures, anti-downgrade policy, key rotation,
restricted runtime APIs, resource budgets and operational rollback controls. See
`docs/hot-update-boundary.md` for the implemented boundary and explicit gaps.

