# TODO

## Known Issues

- [ ] **HUD overflow on zoom >100%** — When scaling above 100%, the lower HUD boundary extends past the lower screen boundary. HUD max dimensions shall be capped to screen resolution. Current `overflow:hidden` / `max-width:100vw` / `max-height:100vh` CSS mitigation may not fully contain all cases.
