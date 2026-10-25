import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { defineConfig } from 'vitepress'
import enUS from '../locales/en-US.json'
import zhCN from '../locales/zh-CN.json'

const repository = 'https://github.com/kusutori/Tonarink'
const directory = dirname(fileURLToPath(import.meta.url))

// GitHub Pages reports the project base without a trailing slash.
// Keep one so head links do not become /Tonarinklogo.svg.
function withTrailingSlash(value: string) {
  const prefixed = value.startsWith('/') ? value : `/${value}`
  return prefixed.endsWith('/') ? prefixed : `${prefixed}/`
}

const base = withTrailingSlash(process.env.VITEPRESS_BASE ?? '/Tonarink/')

export default defineConfig({
  title: 'Tonarink',
  description: enUS.description,
  lang: 'zh-CN',
  base,
  cleanUrls: true,
  srcDir: './content',
  srcExclude: ['README.md'],
  // The build root is srcDir, so the default public folder would be content/public.
  vite: {
    publicDir: resolve(directory, '../public')
  },
  rewrites: {
    'zh-CN/:path*': ':path*',
    'en-US/:path*': 'en/:path*'
  },
  head: [
    ['link', { rel: 'icon', type: 'image/svg+xml', href: `${base}logo.svg` }],
    ['meta', { name: 'theme-color', content: '#009ba3' }],
    ['meta', { property: 'og:type', content: 'website' }],
    ['meta', { property: 'og:title', content: 'Tonarink' }],
    ['meta', { property: 'og:description', content: zhCN.openGraphDescription }]
  ],
  locales: {
    root: {
      label: zhCN.languageLabel,
      lang: 'zh-CN',
      title: 'Tonarink',
      description: zhCN.description,
      themeConfig: {
        nav: [
          { text: zhCN.nav.home, link: '/' },
          { text: zhCN.nav.showcase, link: '/showcase' },
          { text: zhCN.nav.guide, link: '/guide/getting-started' },
          { text: zhCN.nav.download, link: `${repository}/releases/latest` }
        ],
        sidebar: {
          '/guide/': [
            {
              text: zhCN.sidebar.guide,
              items: [
                { text: zhCN.sidebar.gettingStarted, link: '/guide/getting-started' },
                { text: zhCN.sidebar.downloads, link: '/guide/downloads' }
              ]
            }
          ]
        },
        outline: { label: zhCN.outline },
        docFooter: { prev: zhCN.previousPage, next: zhCN.nextPage },
        footer: {
          message: zhCN.footer.message,
          copyright: zhCN.footer.copyright
        }
      }
    },
    en: {
      label: enUS.languageLabel,
      lang: 'en-US',
      link: '/en/',
      title: 'Tonarink',
      description: enUS.description,
      themeConfig: {
        nav: [
          { text: enUS.nav.home, link: '/en/' },
          { text: enUS.nav.showcase, link: '/en/showcase' },
          { text: enUS.nav.guide, link: '/en/guide/getting-started' },
          { text: enUS.nav.download, link: `${repository}/releases/latest` }
        ],
        sidebar: {
          '/en/guide/': [
            {
              text: enUS.sidebar.guide,
              items: [
                { text: enUS.sidebar.gettingStarted, link: '/en/guide/getting-started' },
                { text: enUS.sidebar.downloads, link: '/en/guide/downloads' }
              ]
            }
          ]
        },
        outline: { label: enUS.outline },
        docFooter: { prev: enUS.previousPage, next: enUS.nextPage },
        footer: {
          message: enUS.footer.message,
          copyright: enUS.footer.copyright
        }
      }
    }
  },
  themeConfig: {
    logo: '/logo.svg',
    socialLinks: [{ icon: 'github', link: repository }],
    search: {
      provider: 'local'
    }
  }
})
