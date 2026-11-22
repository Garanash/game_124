# Realmforge

Кроссплатформенная MMORPG по правилам D&D 5e (SRD 5.1): открытый мир в изометрии, пошаговые бои
на сетке, 47 рас со своими стартовыми зонами, классы через наставников, мировоззрение, рюкзак с
синергиями и слиянием предметов, профессии, дома, офлайн-задания, инстансы на 1/3/5/25 игроков, PvP.

## Состав репозитория

| Путь | Что внутри |
| --- | --- |
| `server/src/Shared.Rules` | Правила игры (netstandard2.1): кубы, персонаж, бой, рюкзак, квесты, профессии, PvP. Общие для сервера и клиента |
| `server/src/Shared.Protocol` | Сетевые сообщения MemoryPack |
| `server/src/Shared.Content` | Загрузка, валидация и кодогенерация YAML-контента, заготовки новых зон |
| `server/src/Server.*` | Gateway (HTTP API), Silo (Orleans-гранты), Zone (симуляция зоны, UDP/WebSocket), Store (магазин) |
| `client/` | Unity 6 URP клиент; логика в `client/Client.Core` и тестируется без Unity |
| `content/` | Весь игровой контент в YAML |
| `infra/` | Dockerfile, docker-compose, SQL-схема, манифесты Kubernetes и Agones |
| `tools/` | ContentTool, BotClient, синхронизация с Unity, нарезка портретов |

## Быстрый старт

```bash
dotnet test Realmforge.sln                                  # все тесты
docker compose -f infra/docker-compose.yml up --build       # Postgres, Redis, NATS и все сервисы
dotnet run --project tools/BotClient -- http://127.0.0.1:5080   # бот проходит путь игрока
```

Порты: gateway `5080`, store `5090`, зона `7777/udp` и WebSocket `7778`.

Клиент: выполнить `tools/sync_unity.sh` (собирает общие DLL и копирует контент), затем открыть `client/`
в Unity `6000.0.30f1`.

Веб-клиент (Three.js, играет в браузере с тем же сервером):

```bash
cd client-web && npm install
npm run dev                          # http://localhost:5173, /api проксируется на gateway :5080
npm test                             # юнит-тесты клиента
node scripts/combat-bot.mjs elf      # смоук-тест: бой с манекеном по JSON-протоколу и проверка опыта
```

## Контент и новые локации

```bash
dotnet run --project tools/ContentTool -- validate content
dotnet run --project tools/ContentTool -- codegen content server/src/Shared.Content/Generated/ContentIds.g.cs
dotnet run --project tools/ContentTool -- new-zone content <zoneId> <continentId> <kind> "<Название>" [связанная_зона]
```

Выпуск новой локации:

1. `new-zone` создаёт `content/zones/<zoneId>.yaml` с порталом обратно в связанную зону.
2. Добавить портал в новую зону со стороны связанной зоны, расставить мобов, ресурсы и NPC.
3. Создать сцену Unity с адресом `scenes/<continentId>/<zoneId>` и пометить её Addressable: зона приходит
   клиенту отдельным бандлом, обновлять приложение не нужно.
4. `validate` и `codegen`, затем поднять `version` в `content/manifest.yaml`. CI проверяет оба шага.

Функции можно включать и выключать без релиза через секцию `Features` конфигурации
(`Features__Flags__store=false`). Gateway отдаёт версию контента и список включённых функций на `/content/version`.

## Монетизация

Продаются только косметика и удобства (скины, слоты рюкзака, вкладки сундука), без влияния на силу персонажа.
Store-сервис проверяет покупки на сервере: Xsolla — по подписи вебхука, Apple/Google/Steam — через
интерфейс `IReceiptValidator`, реализации которого надо подключить при выходе на эти платформы.
Повторная доставка одной транзакции не начисляет покупку дважды. В разработке работает `Store__AllowDevPurchases=true`.

## Перед коммерческим релизом

`validate` выдаёт предупреждения по расам, которые не входят в SRD или принадлежат третьим лицам
(например, пандарен — интеллектуальная собственность Blizzard). Их нужно переименовать или переработать.
