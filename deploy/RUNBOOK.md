# IsLabApp Deploy Runbook

Все команды выполняются на сервере из каталога деплоя:

```bash
cd "$DEPLOY_PATH"
```

## Статус сервисов

```bash
docker compose ps
```

Ожидаемое состояние: сервисы `app` и `mssql` находятся в статусе `Up`.

## Логи

Посмотреть последние строки логов всех сервисов:

```bash
docker compose logs --tail=100
```

Смотреть логи приложения в реальном времени:

```bash
docker compose logs -f --tail=100 app
```

Смотреть логи SQL Server:

```bash
docker compose logs -f --tail=100 mssql
```

## Проверка доступности

```bash
curl -fsS http://127.0.0.1:5000/health
curl -fsS http://127.0.0.1:5000/version
curl -fsS http://127.0.0.1:5000/db/ping
```

Ожидаемо:

- `/health` возвращает `status: ok`.
- `/version` возвращает имя и версию приложения.
- `/db/ping` возвращает `status: ok`, если приложение подключается к базе.

## Обновление

1. Изменить тег образа в `.env`:

```bash
APP_IMAGE=ghcr.io/keqwerty/lab4:<new-tag>
```

2. Скачать новый образ и перезапустить приложение:

```bash
docker compose pull app
docker compose up -d app
docker compose ps
```

3. Проверить доступность:

```bash
curl -fsS http://127.0.0.1:5000/health
curl -fsS http://127.0.0.1:5000/version
curl -fsS http://127.0.0.1:5000/db/ping
```

## Откат

1. Вернуть предыдущий тег образа в `.env`:

```bash
APP_IMAGE=ghcr.io/keqwerty/lab4:<previous-tag>
```

2. Скачать образ и перезапустить приложение:

```bash
docker compose pull app
docker compose up -d app
docker compose ps
```

3. Проверить `/health`, `/version`, `/db/ping`.

## Бэкап

Бэкапы хранятся на сервере в каталоге:

```text
$DEPLOY_PATH/backups
```

Создать бэкап базы:

```bash
source .env
mkdir -p backups
timestamp="$(date +%Y%m%d_%H%M%S)"
backup_name="${DB_NAME}_${timestamp}.bak"

docker compose exec -T mssql mkdir -p /var/opt/mssql/backup
docker compose exec -T mssql /opt/mssql-tools18/bin/sqlcmd \
  -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" \
  -Q "BACKUP DATABASE [$DB_NAME] TO DISK = N'/var/opt/mssql/backup/$backup_name' WITH INIT, COMPRESSION"

docker compose cp "mssql:/var/opt/mssql/backup/$backup_name" "backups/$backup_name"
ls -lh "backups/$backup_name"
```

## Проверка восстановления

Быстрая проверка, что файл бэкапа читается SQL Server:

```bash
source .env
backup_name="<backup-file-name>.bak"

docker compose cp "backups/$backup_name" "mssql:/var/opt/mssql/backup/restore_check.bak"
docker compose exec -T mssql /opt/mssql-tools18/bin/sqlcmd \
  -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" \
  -Q "RESTORE VERIFYONLY FROM DISK = N'/var/opt/mssql/backup/restore_check.bak'"
```

Полная проверка восстановления выполняется в отдельную тестовую базу:

```bash
source .env
backup_name="<backup-file-name>.bak"
restore_db="${DB_NAME}_restore_check"

docker compose cp "backups/$backup_name" "mssql:/var/opt/mssql/backup/restore_check.bak"
docker compose exec -T mssql /opt/mssql-tools18/bin/sqlcmd \
  -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" \
  -Q "IF DB_ID('$restore_db') IS NOT NULL BEGIN ALTER DATABASE [$restore_db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$restore_db]; END;
      RESTORE DATABASE [$restore_db]
      FROM DISK = N'/var/opt/mssql/backup/restore_check.bak'
      WITH MOVE '$DB_NAME' TO '/var/opt/mssql/data/${restore_db}.mdf',
           MOVE '${DB_NAME}_log' TO '/var/opt/mssql/data/${restore_db}_log.ldf',
           REPLACE;
      SELECT name, state_desc FROM sys.databases WHERE name = '$restore_db';
      ALTER DATABASE [$restore_db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
      DROP DATABASE [$restore_db];"
```

Если логические имена файлов базы отличаются от `$DB_NAME` и `${DB_NAME}_log`, сначала посмотрите их:

```bash
docker compose exec -T mssql /opt/mssql-tools18/bin/sqlcmd \
  -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" \
  -Q "RESTORE FILELISTONLY FROM DISK = N'/var/opt/mssql/backup/restore_check.bak'"
```

## Политика хранения бэкапов

Учебная политика хранения:

- хранить последние 5 бэкапов в `$DEPLOY_PATH/backups`;
- дополнительно удалять бэкапы старше 14 дней;
- удаление выполнять после успешного создания нового бэкапа.

Удалить все бэкапы, кроме последних 5:

```bash
ls -1t backups/*.bak 2>/dev/null | tail -n +6 | xargs -r rm --
```

Удалить бэкапы старше 14 дней:

```bash
find backups -type f -name '*.bak' -mtime +14 -delete
```
