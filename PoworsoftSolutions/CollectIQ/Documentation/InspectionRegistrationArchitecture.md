# CollectIQ Inspection Registration Architecture

## Purpose

All inspection modules must operate on the same physical card coordinate system even when the phone moves, rotates, changes distance, or is not parallel to the card.

## Shared pipeline

1. `ICardGeometryService` detects the four physical outer card corners in each raw image.
2. `ICardRegistrationService` calculates an independent projective homography for every image and maps those four corners to the same 750 x 1050 canonical rectangle.
3. A constrained fine-registration pass aligns each rectified image to the reference image. It can correct small residual rotation, uniform scale, and X/Y translation caused by imperfect corner localization.
4. Registration diagnostics are generated before inspection calculations run.
5. Surface, Centering, Corners, and Edges can reuse these services.

## Important rule

The homography performs the large correction. Fine registration is intentionally constrained and should only correct small residual errors. If a future frame requires a large correction, it should be rejected and recaptured rather than silently distorted.

## Current canonical coordinate system

- Width: 750 px
- Height: 1050 px
- Top-left: (0, 0)
- Top-right: (749, 0)
- Bottom-right: (749, 1049)
- Bottom-left: (0, 1049)

A later high-resolution inspection mode can increase this while keeping the same normalized coordinate convention.
