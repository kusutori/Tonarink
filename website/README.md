# Tonarink website

The product website is built with VitePress and Bun.

English source pages live under `content/en-US`; translated pages use their
Crowdin locale under `content/<locale>`. Site chrome strings follow the same
convention in `locales`. VitePress rewrites the source folders to the public
Chinese `/` and English `/en/` routes.

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
placeholder in both localized `showcase.md` files:

- `public/media/tonarink-demo.mp4`
- `public/screenshots/send.webp`
- `public/screenshots/receive.webp`
- `public/screenshots/windows-integration.webp`
