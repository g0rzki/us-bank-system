# Frontend — US Bank System

React + Vite + TypeScript SPA dla US Bank System.

## Stack

- React 18
- Vite
- TypeScript
- React Router
- Axios

## Uruchomienie

Skopiuj `.env.example` i uzupełnij:

```bash
cp .env.example .env
```

Uruchom aplikację:

```bash
cd frontend
npm install
npm run dev
```

Aplikacja dostępna pod `http://localhost:5173` (dev) lub `http://localhost:3000` (Docker/nginx).

## Struktura

```
frontend/
├── src/
│   ├── pages/
│   ├── components/
│   ├── api/
│   │   └── client.ts
│   └── main.tsx
├── .env.example
├── index.html
└── vite.config.ts
```