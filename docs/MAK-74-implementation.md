# MAK-74 Implementation Note

## What Was Done
- Added file upload allowlist validation for image MIME types and extensions at pre-sign URL generation.
- Added server-side S3 metadata validation before saving a spot submission.
- Switched the upload flow from presigned PUT to presigned POST.
- Added upload-time `content-length-range` enforcement in the S3 POST policy.

## Implemented
- Max file size limit (`SpotSubmissionStorageOptions.MaxUploadBytes`, default 5 MB).
- MIME validation allowlist (`image/jpeg`, `image/png`, `image/webp`).
- Unsupported extension rejection (`.jpg`, `.jpeg`, `.png`, `.webp`).
- MIME/extension consistency checks.
- Upload-time S3 POST policy constraints for `Content-Type` and file size.
- Submission-time verification of `photoUrl` and `photoStorageKey` mapping.

## Pending / Notes
- Submission-time S3 metadata validation remains in place as defense in depth even though the upload is now constrained at POST policy time.
