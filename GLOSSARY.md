# Glossary

## Package delisting

Requesting that a NuGet source unlist chosen versions of a package.

## Delist planning

Deciding which versions need deleting vs are already delisted (existence check, listed-state partition, skip logic). Sits in front of the delete backend.

## Delete backend

The adapter behind the Delist planning seam that performs the single-version delete.
The CLI's `--backend` flag selects among delete backends (HTTP, SDK).

## Delist plan

The three buckets delist planning partitions requested versions into: *to delist* (exists and is listed), *already delisted* (exists but unlisted), *not on server* (absent from the server's metadata).

## Version outcome

The closed result vocabulary for one version's attempt: *delisted*, *already delisted*, *not on server*, *failed*, *rate limited*, or *not attempted* (stopped before dispatch). Shared by the delete backend, CLI output, and exit codes.

## Dry run

A run that resolves and prints the delist plan without sending any delete request; exits non-zero when any requested version is not on the server.

