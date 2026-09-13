# Tonarink website

The product website is built with VitePress and Bun.

```powershell
cd website
bun install
bun run docs:dev
```

Run `bun run docs:build` for a production build or `bun run docs:preview` to
preview that build locally. GitHub Pages deploys the site from `main` through
`.github/workflows/pages.yml`.

## Showcase media

The showcase currently contains placeholders for media still being produced.
When the assets are ready, add them under `public` and replace the matching
placeholder in both `showcase.md` and `en/showcase.md`:

- `public/media/tonarink-demo.mp4`
- `public/screenshots/send.webp`
- `public/screenshots/receive.webp`
- `public/screenshots/windows-integration.webp`
