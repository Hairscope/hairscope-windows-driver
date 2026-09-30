# Changelog

All notable changes to the Hairscope Agent will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- SignPath.io integration for trusted code signing
- GitHub Actions workflow for automated build, sign, and release
- Self-signed certificate generation for local development
- Local signing script for development testing
- MIT License

### Changed
- Updated project metadata for open source distribution
- Added deterministic builds for reproducible signatures

## [1.0.8] - 2024-01-15

### Added
- Initial release of Hairscope Agent
- USB device detection for trichoscopy probe
- Hardware button capture via USBPcap
- WebSocket server for web app communication
- Windows Service installation via Inno Setup
- Automatic USBPcap driver installation

### Security
- Embedded device configuration (not shipped as plaintext)
- Service runs as LocalSystem for USBPcap access

## [1.0.0] - 2023-11-01

### Added
- Initial internal release
- Basic probe detection and button handling

---

## Release Process

For maintainers:

1. Update version in `src/HairscopeAgent/HairscopeAgent.csproj`
2. Update version in `installer/HairscopeAgent.iss`
3. Update this CHANGELOG.md
4. Commit: `git commit -am "Release v1.0.9"`
5. Tag: `git tag v1.0.9`
6. Push: `git push origin v1.0.9`

The GitHub Actions workflow will automatically:
- Build and publish the executable
- Build the Inno Setup installer
- Sign both artifacts via SignPath.io
- Create a GitHub Release with signed downloads