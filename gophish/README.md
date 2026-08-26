# Gophish — turnkey Docker-деплой

Готовый «под ключ» стек [Gophish](https://github.com/gophish/gophish) для
**авторизованных** фишинг-симуляций и обучения сотрудников информационной
безопасности. Разворачивается одной командой, HTTPS настраивается автоматически.

> ⚠️ **Только для легального использования.** Запускайте фишинг-симуляции
> исключительно против инфраструктуры и сотрудников, на тестирование которых у
> вас есть **письменное разрешение** владельца (например, в рамках программы
> security-awareness или согласованного пентеста). Использование против третьих
> лиц без согласия незаконно.

## Что внутри

| Сервис    | Роль                                                                 |
|-----------|---------------------------------------------------------------------|
| `caddy`   | Reverse-proxy, единственный публикует порты `80/443`, авто-Let's Encrypt |
| `gophish` | Сам Gophish (SQLite), порты наружу **не** выставляет                 |

```
Интернет ─443/80─> Caddy ─┬─ admin.DOMAIN ─> gophish:3333 (админка, TLS-passthrough)
                          └─ phish.DOMAIN ─> gophish:80   (лендинги)
```

Конфигурация Gophish задаётся штатными env-переменными образа (его `docker/run.sh`
патчит `config.json` через `jq`) — отдельных конфиг-файлов монтировать не нужно.
Self-signed сертификат для админки Gophish генерирует сам; Caddy проксирует к нему
с пропуском проверки сертификата и отдаёт браузеру валидный Let's Encrypt.

## Требования

- Linux-сервер с Docker и Docker Compose v2.
- Домен и **DNS A-записи**, указывающие на IP сервера:
  - `admin.example.com` → админ-панель;
  - `phish.example.com` → phishing-сервер (этот хост уходит в ссылки писем).
- Открытые входящие порты `80` и `443` (Let's Encrypt использует `80` для
  ACME-проверки, поэтому он обязателен).

## Быстрый старт

```bash
cd gophish
cp .env.example .env
nano .env                 # укажи свои домены и e-mail
docker compose up -d
```

Проверить статус и получить начальный пароль администратора:

```bash
docker compose ps
docker compose logs gophish | grep -i "please login\|admin"
```

- Если в `.env` задан `GOPHISH_INITIAL_ADMIN_PASSWORD` — используй его.
- Если оставил пустым — Gophish напечатает случайный пароль в логах при первом
  запуске (строка вида `Please login with the username admin and the password ...`).

Открой **`https://admin.example.com`**, зайди под `admin`, сразу **смени пароль**
(Settings) и настрой:

1. **Sending Profiles** — SMTP-сервер для отправки писем.
2. **Landing Pages** — страница-приманка (можно импортировать по URL).
3. **Email Templates** — шаблон письма (в ссылке используй `{{.URL}}`).
4. **Users & Groups** — список целей (только авторизованные!).
5. **Campaigns** — запуск и статистика в реальном времени.

## Проверка деплоя

```bash
docker compose config      # валидность compose + подстановка .env
docker compose ps          # gophish и caddy = running
```

- `https://admin.example.com` открывается с доверенным сертификатом, логин без
  ошибок 403/CSRF.
- `https://phish.example.com` до создания лендинга отдаёт `404 page not found` —
  это нормально (phishing-сервер жив).

## Эксплуатация

**Бэкап** (вся БД и конфиг лежат в volume `gophish_data`):

```bash
docker run --rm -v gophish_gophish_data:/data -v "$PWD":/backup alpine \
  tar czf /backup/gophish-backup.tgz -C /data .
```

**Восстановление** — распаковать архив обратно в volume при остановленном стеке.

**Обновление версии**: поменяй тег `gophish/gophish:vX.Y.Z` в `docker-compose.yml`,
затем `docker compose pull && docker compose up -d`.

**Логи**: `docker compose logs -f gophish` / `... caddy`.

**Остановка**: `docker compose down` (данные в volume сохраняются;
`down -v` — удалит их безвозвратно).

## Заметки

- **Реальный IP цели за прокси.** Caddy передаёт `X-Forwarded-For`. Если в
  событиях кампании нужен корректный клиентский IP, проверьте актуальные опции
  доверия proxy-заголовкам в документации вашей версии Gophish.
- **`CONTACT_ADDRESS`** в `.env` можно заполнить адресом, на который получатели
  могут пожаловаться/уточнить — Gophish показывает его на служебных страницах.
- **Staging-сертификаты.** Для отладки TLS без лимитов Let's Encrypt
  раскомментируйте `acme_ca ...staging...` в `Caddyfile` (потом верните обратно
  и очистите volume `caddy_data`).
- **Кастомизация конфига Gophish.** Все параметры — через env в
  `docker-compose.yml` (`ADMIN_LISTEN_URL`, `ADMIN_USE_TLS`,
  `ADMIN_TRUSTED_ORIGINS`, `PHISH_LISTEN_URL`, `PHISH_USE_TLS`, `DB_FILE_PATH`,
  `CONTACT_ADDRESS`). Их применяет `docker/run.sh` образа при старте.

## Файлы

```
gophish/
├── docker-compose.yml      # оркестрация gophish + caddy
├── Caddyfile               # reverse-proxy + авто-HTTPS
├── .env.example            # шаблон окружения (копировать в .env)
├── .gitignore              # .env и локальные секреты вне git
└── README.md
```
