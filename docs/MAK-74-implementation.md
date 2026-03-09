# MAK-74 Implementation Note

## What Was Done
- Added file upload allowlist validation for image MIME types and extensions at pre-sign URL generation.
- Added server-side S3 metadata validation before saving a spot submission.

## Implemented
- Max file size limit (`SpotSubmissionStorageOptions.MaxUploadBytes`, default 10 MB).
- MIME validation allowlist (`image/jpeg`, `image/png`, `image/webp`).
- Unsupported extension rejection (`.jpg`, `.jpeg`, `.png`, `.webp`).
- MIME/extension consistency checks.
- Submission-time verification of `photoUrl` and `photoStorageKey` mapping.

## Pending / Notes
- Size is enforced during submission validation (S3 metadata check), not at upload transport policy level.
- Pre-signed POST with `content-length-range` can be added later for stricter upload-time enforcement.
