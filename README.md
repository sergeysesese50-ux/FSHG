# FSHG
# Мобильная онлайн-рыбалка

Unity-клиент и сервер на Nakama в одном репозитории. План и решения:
https://claude.ai/artifact/LFTA3HQvrRmvpxPSBhKcNJ

## Структура

```
client/        Unity-проект (создаётся через Unity Hub, см. ниже)
server/        Nakama: модули на TypeScript, docker-compose для локального запуска
shared/data/   fish.json, tackle.json, locations.json — единый источник правды
infra/         деплой ru-1 и us-1, каталог серверов servers.json
docs/          геймдизайн и принятые решения
```

## Первый запуск на новой машине

1. Установить Git и Git LFS, затем один раз выполнить:

   ```bash
   git lfs install
   ```

2. Прописать драйвер слияния сцен Unity (путь — к своей установке Unity):

   ```bash
   git config merge.unityyamlmerge.name "Unity SmartMerge"
   git config merge.unityyamlmerge.driver "'C:/Program Files/Unity/Hub/Editor/<версия>/Editor/Data/Tools/UnityYAMLMerge.exe' merge -h -p --force --fallback none %O %B %A %A"
   ```

   `-h` и `--fallback none` — без всплывающих окон и внешних программ слияния,
   `--force` — временные файлы git без расширения тоже считаются сценами.
   Если обе ветки поменяли одно и то же поле, git сообщит о конфликте, а в файле
   окажется значение из вливаемой ветки — проверь сцену в Unity перед коммитом.

3. Установить Unity Hub и ту версию Unity, что указана в
   `client/ProjectSettings/ProjectVersion.txt`. Открыть папку `client/` через Hub.

4. Для сервера — Docker Desktop:

   ```bash
   cd server
   docker compose up --build
   ```

   Клиентский API — http://127.0.0.1:7350, консоль — http://127.0.0.1:7351
   (локальный логин `admin` / `password`, только для разработки).

## Как работаем

- Ветки `feature/*`, маленькие PR в `main`, одно одобрение.
- `git pull` перед началом дня.
- Одну сцену вдвоём одновременно не правим.