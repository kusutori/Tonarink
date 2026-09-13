import { defineConfig } from 'vitepress'

const repository = 'https://github.com/kusutori/Tonarink'
const base = process.env.VITEPRESS_BASE ?? '/Tonarink/'

export default defineConfig({
  title: 'Tonarink',
  description: 'Fast, private local sharing for Windows',
  lang: 'zh-CN',
  base,
  cleanUrls: true,
  srcExclude: ['README.md'],
  head: [
    ['link', { rel: 'icon', type: 'image/svg+xml', href: `${base}logo.svg` }],
    ['meta', { name: 'theme-color', content: '#009ba3' }],
    ['meta', { property: 'og:type', content: 'website' }],
    ['meta', { property: 'og:title', content: 'Tonarink' }],
    ['meta', { property: 'og:description', content: '让附近的设备，自然连起来。' }]
  ],
  locales: {
    root: {
      label: '简体中文',
      lang: 'zh-CN',
      title: 'Tonarink',
      description: '安全、快速的局域网传输工具',
      themeConfig: {
        nav: [
          { text: '首页', link: '/' },
          { text: '功能展示', link: '/showcase' },
          { text: '使用指南', link: '/guide/getting-started' },
          { text: '下载', link: `${repository}/releases/latest` }
        ],
        sidebar: {
          '/guide/': [
            {
              text: '使用指南',
              items: [
                { text: '开始使用', link: '/guide/getting-started' },
                { text: '选择下载版本', link: '/guide/downloads' }
              ]
            }
          ]
        },
        outline: { label: '本页内容' },
        docFooter: { prev: '上一页', next: '下一页' },
        footer: {
          message: '基于 Apache-2.0 许可证发布 · LocalSend 协议的非官方兼容实现',
          copyright: 'Copyright © 2026 Tonarink contributors'
        }
      }
    },
    en: {
      label: 'English',
      lang: 'en-US',
      link: '/en/',
      title: 'Tonarink',
      description: 'Fast, private local sharing for Windows',
      themeConfig: {
        nav: [
          { text: 'Home', link: '/en/' },
          { text: 'Showcase', link: '/en/showcase' },
          { text: 'Guide', link: '/en/guide/getting-started' },
          { text: 'Download', link: `${repository}/releases/latest` }
        ],
        sidebar: {
          '/en/guide/': [
            {
              text: 'Guide',
              items: [
                { text: 'Getting started', link: '/en/guide/getting-started' },
                { text: 'Choose a download', link: '/en/guide/downloads' }
              ]
            }
          ]
        },
        outline: { label: 'On this page' },
        docFooter: { prev: 'Previous page', next: 'Next page' },
        footer: {
          message: 'Released under Apache-2.0 · An unofficial LocalSend protocol implementation',
          copyright: 'Copyright © 2026 Tonarink contributors'
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
