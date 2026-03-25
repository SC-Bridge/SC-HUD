# TODO

## Pending

- [ ] **Test CI pipeline** — push a `v0.3.0` tag to verify the build workflow produces valid `.zip` and `.msi` artifacts on GitHub Actions.
- [ ] **MSI icon** — add `src/Assets/icon.ico` as the installer icon in `installer.wxs` (WiX `<Icon>` + `<Property Id="ARPPRODUCTICON">`).

## Known Issues

- [ ] **HUD overflow on zoom >100%** — When scaling above 100%, zoomed content can extend past the lower screen boundary. CSS overflow mitigation was removed (it broke scrolling); the WebView2 HWND boundary provides some clipping but does not fully contain all cases. Proper fix: cap overlay dimensions to screen resolution before applying zoom.
