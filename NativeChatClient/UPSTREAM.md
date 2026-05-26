# NativeChat Upstream Notes

## Purpose

NativeChatClient is the first-party local chat client used by Transparent Twitch Chat Overlay. It was originally forked and adapted from Cyan Chat, which was originally based on jChat.

## Upstream Sources

- Cyan Chat: https://github.com/Johnnycyan/cyan-chat
- jChat: https://github.com/giambaJ/jChat

## Upstream Tracking

- Upstream project: Cyan Chat
- Upstream remote: https://github.com/Johnnycyan/cyan-chat
- Original imported baseline: 26868c092c1d7752793b7bfccfe4ad223463eff6
- Last reviewed upstream commit: 26868c092c1d7752793b7bfccfe4ad223463eff6
- Last reviewed date:
- Notes: NativeChat has diverged substantially. Future upstream changes are reviewed and ported selectively.

## Local Ownership

This copy is maintained as first-party TTCO code. Upstream changes are reviewed and ported selectively rather than merged blindly.

## Major Local Changes

- Runs as a local WebView2-hosted chat client for TTCO.
- Receives settings from the WPF host through WebView2 messages.
- Uses TTCO settings instead of relying only on generated URL parameters.
- Uses local packaging and version metadata for WPF extraction and repair.
- Builds into files that are zipped and embedded by the WPF app.
- Supports TTCO chat filters, sound, highlights, and appearance settings.
- Removes or changes OBS/browser-source assumptions where needed.

## Compatibility Contract

The WPF host and NativeChat client communicate with WebView2 messages.

- Client to host: `NativeChatReady`
- Host to client: `config`
- Host to client: `credentials`

Current protocol version: `1`

Before changing this contract, update both the WPF provider and the NativeChat client.

## Update Procedure

1. Check upstream for relevant fixes.
2. Compare upstream against the recorded baseline.
3. Port only the changes that make sense for TTCO.
4. Avoid reintroducing URL-only, OBS-only, or remote-hosted assumptions.
5. Run `npm ci` when dependencies need to be restored from the lock file.
6. Bump `package.json` and `package-lock.json` when users should receive a new asset pack.
7. Run `npm.cmd run build` on Windows, or `npm run build` from a non-PowerShell shell.
8. Verify `dist/version.json` and `dist/nativechat-manifest.json`.
9. Build the WPF app and confirm the embedded NativeChat zip is updated.
10. Test NativeChat inside WebView2.
11. Update this file with the new upstream baseline or reviewed date.

## Intentional Divergences

- TTCO owns the settings UI and persistence.
- The WPF app owns install, repair, rollback, and update behavior.
- OAuth tokens must not be logged.
- Runtime scripts should be bundled or pinned, not loaded from `latest` CDN URLs.
- The overlay must remain transparent and lightweight.

## License Notes

- Preserve upstream attribution.
- Keep license notices for inherited code and bundled third-party libraries.
- Keep bundled third-party library notices when build output includes them.
