import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import fs from 'fs'
import path from 'path'

function loadRootEnv(): Record<string, string> {
  const rootEnvPath = path.resolve(__dirname, '../.env')
  if (!fs.existsSync(rootEnvPath)) return {}
  return Object.fromEntries(
    fs.readFileSync(rootEnvPath, 'utf-8')
      .split('\n')
      .filter(line => line && !line.startsWith('#') && line.includes('='))
      .map(line => {
        const [key, ...rest] = line.split('=')
        return [key.trim(), rest.join('=').trim()]
      })
  )
}

const rootEnv = loadRootEnv()

export default defineConfig({
  plugins: [react()],
  define: {
    'import.meta.env.VITE_API_URL': JSON.stringify(
      process.env.VITE_API_URL ?? rootEnv['API_URL'] ?? 'http://localhost:5100'
    ),
  },
})
