# TODO

## Known Issues

- [ ] **HUD overflow on zoom >100%** — When scaling above 100%, zoomed content can extend past the lower screen boundary. CSS overflow mitigation was removed (it broke scrolling); the WebView2 HWND boundary provides some clipping but does not fully contain all cases. Proper fix: cap overlay dimensions to screen resolution before applying zoom.
