import { defineConfig } from 'wxt'

const apiOrigin = process.env.READINGHUB_API_ORIGIN ?? 'http://localhost:5080'

export default defineConfig({
  srcDir: 'src',
  modules: ['@wxt-dev/module-react'],
  manifest: {
    name: 'ReadingHub',
    description: 'Save bookmarks and sync reading progress with your ReadingHub server.',
    key: 'MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAlj7yEynyCixCPd1VZKjmM6Ey1L9UH1SOAJvt/pnRbvydNrtjpOrUPDnMJwcqCml85R5DwTEoMybdOrSOJTsXQsrTThH5kkn+Y0vaYOE8yOcqRfWBsQqDKVZRFTeeSttcYHSxwGVLYiD8s4VogeWH8mhOuaZ2M1RRGLwDCxj0Sl+ZvluDatlcKB1hcFoOLWilnJErDdK7uske6hJvJcCDbY4c8blhFExOkysS+7mBXE26PMkWTQbvY/K1bR3G6ZdtfQj9+4hCQB1dKvQSWeU3gXsVn553b/yz+Zuxv/GavKn892PHUj+PLwqPy1Rrg2FQNCLYPtGQ0plFRol8fLko2wIDAQAB',
    permissions: ['storage', 'tabs', 'activeTab', 'scripting', 'alarms', 'identity'],
    host_permissions: [`${apiOrigin}/*`],
    optional_host_permissions: [
      'https://www.royalroad.com/*',
      'https://royalroad.com/*',
      'https://www.webnovel.com/*',
      'https://webnovel.com/*',
      '<all_urls>',
    ],
    action: {
      default_title: 'ReadingHub',
    },
  },
})
