# Changelog

All notable changes to the XiaoZhi plugin will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Initial implementation of XiaoZhi WebSocket server plugin
- Support for XiaoZhi protocol versions 1, 2, and 3
- OPUS audio codec support for efficient audio streaming
- WebSocket-based bidirectional audio communication
- Automatic middleware registration via IBotSharpAppPlugin
- Integration with BotSharp Realtime API
- Support for client hello handshake and version negotiation
- Configuration settings for authentication, audio parameters, and endpoint
- Compatible with xiaozhi-esp32 and other XiaoZhi clients
- Comprehensive README with setup instructions and protocol documentation
- Example configuration file

### Technical Details
- Direct WebSocket message handling for binary audio support
- Binary protocol packet parsing for versions 1, 2, and 3
- JSON-based control messages (hello, wake_word_detected, start_listening, etc.)
- Integration with IRealtimeHub for LLM realtime conversation
- Base64 audio encoding for compatibility with realtime completers
