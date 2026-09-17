# Glossary

## Package delisting

Requesting that a NuGet source unlist chosen versions of a package.

## Delist planning

Deciding which versions need deleting vs are already delisted (existence check, listed-state partition, skip logic). Sits in front of the delete backend.

## Delete backend

The adapter behind the Delist planning seam that performs the single-version delete. The CLI's `--backend` flag selects among delete backends (HTTP, SDK).
