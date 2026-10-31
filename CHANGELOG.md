# Changelog

## 0.2.4.0

The plugin now adds the Request tab to Jellyfin Web by itself. No index.html edit or extra plugin is needed.

Changes:

- Adds the Request tab loader to Jellyfin Web's index.html as the server sends it to the browser. The file on disk isn't changed, so this works on Docker installs and keeps working after Jellyfin image updates.
- Skips adding the loader if the page already has it, for example from an older manual index.html edit.
- The tab script only runs once even if it's loaded more than one way.

## 0.2.3.0

Rebuild of 0.2.2.0 with the updated `request-entry.js`. The 0.2.2.0 package shipped the old script by mistake, so none of the 0.2.1.0 or 0.2.2.0 navigation changes below reached Jellyfin until this release.

## 0.2.2.0

Puts the Request tab in the Modern layout's Home/Favorites tab bar.

Changes:

- The Request tab now goes in the Modern layout's Home/Favorites tab bar, styled like the tabs beside it.
- Removed the header button added in 0.2.1.0. It could appear before the tab bar finished loading and stop the tab from being added.
- The avatar menu entry stays, so Requests can still be reached from pages without the tab bar.

## 0.2.1.0

Adds the Requests entry to Jellyfin 12's Modern layout, which has no side drawer on desktop.

Changes:

- Adds a Requests button to the Modern header, next to the user avatar, when there's no Home/Favorites tab bar to add a tab to.
- Adds a Requests item to the user avatar menu, above Sign Out.
- Documents loading the navigation script through the JavaScript Injector plugin, which keeps working on Docker installs and after Jellyfin updates.

## 0.2.0.0

Adds support for Jellyfin 12.x. This build does not load on Jellyfin 10.11.x; servers on 10.11.x should stay on 0.1.0.5, which remains in the plugin repository manifest.

Changes:

- Targets .NET 10 and builds against Jellyfin.Controller and Jellyfin.Model 12.0.0, with targetAbi 12.0.0.0 so the build covers 12.0 through 12.4.
- The request page no longer sends the legacy `X-Emby-Token` header. Jellyfin 12 disables legacy authorization by default, and the standard `Authorization: MediaBrowser` header was already being sent.
- Release scripts, manifest generators and the CI workflow default to `net10.0`, version 0.2.0.0 and targetAbi 12.0.0.0.
- The CI workflow uses the .NET 10 SDK and takes the package name from the plugin version instead of a hard-coded 0.1.0.0.
- Updated install, release and security docs for Jellyfin 12.

## 0.1.0.5

Simplifies installation around the standard Jellyfin plugin install plus one web-loader installer command.

Changes:

- Removed the experimental automatic Plugin Pages and Jellyfin Web self-injection behavior.
- Restored the plugin to server-side APIs, request page, and dashboard settings only.
- Moved the user-facing Jellyfin Web navigation setup to `scripts/install-web-loader.sh`.
- Reworked the README so the standard install steps are at the top.

## 0.1.0.4

Adds a Jellyfin Plugin Pages home-menu entry, matching the integration style used by Jellyfin Enhanced.

Fixes:

- Restores the plugin settings page as the only Jellyfin dashboard plugin page.
- Adds `Request` to Plugin Pages configuration when that integration is installed.
- Keeps Jellyfin Web `index.html` injection as a fallback when the web root is writable.

## 0.1.0.3

Restores the dashboard settings page and moves the user-facing navigation to the Jellyfin Web loader.

Fixes:

- The plugin `Settings` button opens settings again instead of the request page.
- The request entry is injected into Jellyfin Web on plugin startup, similar to Jellyfin Enhanced.
- The dashboard-only `Request` plugin page from `0.1.0.1` and `0.1.0.2` has been removed.

## 0.1.0.2

Aligns the plugin DLL assembly version with the Jellyfin package version.

Fixes:

- Jellyfin no longer reports the loaded plugin assembly as `1.0.0.0`.

## 0.1.0.1

Adds a built-in Jellyfin main-menu `Request` page using Jellyfin's plugin page system.

Fixes:

- Main-menu access no longer requires editing Jellyfin Web's `index.html`.
- Release tooling now emits Jellyfin-compatible MD5 package checksums.

## 0.1.0.0

Initial preview release.

Features:

- Jellyfin-hosted Jellyseerr request page.
- Search and discover media from Jellyseerr.
- Movie and TV detail pages.
- Per-season request buttons for TV.
- Jellyseerr-provided availability and request state.
- Filters for rating, release date, and major streaming providers.
- Sorting by release date, title, and rating.
- Infinite scroll for request results.
- Plugin-served Jellyfin Web navigation entry script.
- Server-side Jellyseerr API forwarding without exposing the API key to browsers.
