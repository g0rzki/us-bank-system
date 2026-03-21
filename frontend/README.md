# Frontend — US Bank System

> **Placeholder** — frontend zostanie zainicjowany w zadaniu US-35 przez `npm create vite@latest`.

## Planowany stack

- React 18
- Vite
- TypeScript
- React Router
- Axios

## Planowana struktura (po inicjalizacji)

```
frontend/
├── src/
│   ├── pages/
│   │   ├── LoginPage.tsx
│   │   ├── DashboardPage.tsx
│   │   ├── TransferPage.tsx
│   │   └── BlikPage.tsx
│   ├── components/
│   ├── api/
│   │   └── client.ts        # axios instance z JWT interceptorem
│   └── main.tsx
├── index.html
├── vite.config.ts
└── package.json
```

## Uruchomienie (po inicjalizacji)

```bash
cd frontend
npm install
npm run dev
```

Aplikacja dostępna pod `http://localhost:5173` (dev) lub `http://localhost:3000` (Docker/nginx).