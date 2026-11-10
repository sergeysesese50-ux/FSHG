# Звуковые харнессы (tools/audio)

Консольные проекты .NET 8, которые синтезируют звук рыбалки без Unity, пишут превью в WAV и меряют их.
Звук игры — палитра «Натуральная» — живёт в `client/Assets/_Game/Scripts/Fishing/Audio` (сборка Fishing.Core);
здесь — прототипы трёх палитр, из которых её выбрали, критик и доказательство, что игра звучит как прототип.

| Папка | Что это | Куда пишет |
|---|---|---|
| `Audio_natural` | Прототип «Натуральной» палитры (выбрана): клипы, решения по кадру, микшер зёрен, два боя FightModel | `.previews/audio/natural` |
| `Audio_cozy` | Прототип «Уютной ASMR» палитры | `.previews/audio/cozy` |
| `Audio_tactile` | Прототип «Тактильной» палитры | `.previews/audio/tactile` |
| `AudioDiag` | Диагностика прежнего звука (`Legacy/` — старые FightSynth.cs и FightCues.cs, в игре их больше нет) | `.previews/audio/current` |
| `AudioCritic` | Объективный критик: громкость, спектр, гудение, ритм, швы петель, A/B-файлы | `out.txt`, `metrics.csv` здесь; `.previews/audio/critic` |
| `AudioCritic/LoopDump` | Выгружает сырые петли всех палитр для проверки швов критиком | папка из аргумента (обычно `AudioCritic/loops`) |
| `AudioIngame` | Код игры (банк, директор, микшер, лимитер) на настоящих боях — то, что слышит игрок | `.previews/audio/ingame`, `out.txt` здесь |
| `Shared/RepoPaths.cs` | Корень репозитория от папки сборки: превью пишутся в `<репозиторий>/.previews/audio/…` | — |

Заметки к каждой палитре — `NOTES.md` в её папке (отчёты на английском, как их писали), исследование — `RESEARCH.md`,
итог сравнения — `AudioCritic/NOTES.md` и `AudioCritic/COMPARE.md`. `out.txt` — замеры последнего прогона.

## Что нужно

- .NET SDK 8. Подойдёт тот, что лежит в Unity: `C:/Program Files/Unity/Hub/Editor/6000.6.8f1/Editor/Data/DotNetSdk/dotnet.exe`.
- Установленный Unity 6000.6.8f1: проекты ссылаются на `UnityEngine.CoreModule.dll` (FightFrame использует Vector3).
  Unity в другом месте — добавить `-p:UnityManaged="<путь>/Editor/Data/Managed/UnityEngine"`.
- Пути к исходникам игры — от корня репозитория (`Directory.Build.props`), абсолютных путей в проектах нет.
- Редактор Unity при этом может быть открыт: харнессы только читают `client/Assets` и ничего туда не пишут.

## Как пересобрать превью

Из корня репозитория (Git Bash; `DN` — путь к dotnet):

```sh
DN="/c/Program Files/Unity/Hub/Editor/6000.6.8f1/Editor/Data/DotNetSdk/dotnet.exe"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

"$DN" run -c Release --project tools/audio/AudioIngame      # звук игры (~11 с) → .previews/audio/ingame + AudioIngame/out.txt
"$DN" run -c Release --project tools/audio/Audio_natural    # ~3 с → .previews/audio/natural
"$DN" run -c Release --project tools/audio/Audio_cozy       # → .previews/audio/cozy
"$DN" run -c Release --project tools/audio/Audio_tactile    # → .previews/audio/tactile
"$DN" run -c Release --project tools/audio/AudioDiag        # прежний звук → .previews/audio/current

# критик: сначала сырые петли, потом разбор всех превью (~7 с)
"$DN" run -c Release --project tools/audio/AudioCritic/LoopDump -- tools/audio/AudioCritic/loops
"$DN" run -c Release --project tools/audio/AudioCritic
```

У каждого проекта первый аргумент — другая папка вывода (у критика — другой корень превью).
Всё детерминировано: повторный прогон даёт те же файлы. `.previews/` и `bin/`, `obj/`, `loops/` в git не попадают.

## Что важно знать

- Прототипы палитр заморожены: это запись того, что слушали и мерили при выборе. Их классы — в пространстве имён
  `Fishing.Unity` и в игру не входят; править звук игры — в `Fishing.Core/Audio`, проверять — `AudioIngame`.
- `AudioIngame` компилирует ровно файлы игры (`Scripts/Fishing/**`, `Lake/FightFrame.cs`, `Lake/FightAudioInput.cs`),
  кадры 60 к/с и звуковые буферы 1024 × 48 кГц идут вперемешку, как главный и звуковой потоки. Метрики — из
  `AudioCritic` (те же функции), в конце `out.txt` — таблица «игра против превью natural».
- Тесты звука (расписание зёрен, лимитер, швы петель, различимость вариантов, защита от NaN и бесконечностей, память банка, петли без периода, гул у предела, приглушение катушки) — `client/Assets/_Game/Tests/EditMode/FightAudioTests.cs`.
