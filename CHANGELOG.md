# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## 0.2.0 - 2026-03-11
### Added
- LOD fade mode support (None, CrossFade, SpeedTree) to reduce popping artifacts during LOD transitions
- Animate cross-fading toggle for smooth automated LOD blending
- `LODGroupHelper.InvalidateCache()` method for manual cache refresh after external LODGroup changes
- Fade mode settings exposed in Preferences UI, per-model LODData overrides, and LODData inspector

### Fixed
- Consistent `ForceLOD(0)/ForceLOD(-1)` initialization pattern in `ModelImporterLODGenerator.CreateLODGroup`
- Null renderer filtering when building LOD arrays for LODGroup setup in `ModelImporterLODGenerator`
- Null safety for renderer arrays in `Extensions.HasLODChain()`
- Null safety for LOD arrays and renderer arrays in `LODGroupExtensions.SetRenderersEnabled`

## 0.1.2 - 2024-05-23
- Adds option to use same material for all LODs
- More Instalod integration fixes
- 
## 0.1.1 - 2024-04-30
- Fix bugs
- Fix Instalod integration

## 0.1.0 - 2024-01-27
- Lots of fixes to remove or replace obsolete stuff
- Some changes to performance
- Fix bugs

## 0.0.2 - 2021-08-13
### Added
- Update SimplygonMeshSimplifier to support Simplygon9 (requires USD 2.0.0-exp.1 package version)

### Fixed
- Fixed WorkingMesh name length termination
- Fixed GetTriangleRange not respecting subMeshCount
- Fixed missing MeshFilter causing Unity editor window updates to stall

### Changed
- Removed MeshDecimator simplifier since the project has been archived

## 0.0.1
- First internal build.

#Contributors
- Amir Ebrahimi
- Elliot Cuzzillo
- Yuangguang Liao