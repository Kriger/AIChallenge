# Модель памяти агента

## Архитектура

Агент использует **трёхуровневую модель памяти**, вдохновлённую когнитивной психологией:

```
┌─────────────────────────────────────────────────────────┐
│                   MemoryManager                          │
│         Единая точка управления памятью                  │
├──────────────┬──────────────────┬───────────────────────┤
│  ShortTerm   │    Working       │     LongTerm          │
│  (диалог)    │    (задача)      │     (знания)          │
│  100 записей │    словарь       │     500 фактов        │
└──────────────┴──────────────────┴───────────────────────┘
```

## Типы памяти

### 1. Краткосрочная память (ShortTermMemory)

**Назначение:** Текущий диалог — хронологический список сообщений.

**Хранение:** `List<MemoryEntry>` — упорядоченный список с автоочисткой старых записей.

**Жизненный цикл:** Сессия диалога. Очищается при `ClearHistory()`.

**Лимит:** 100 записей (настраивается). При превышении удаляются самые старые.

**Что сохраняется:**
- Сообщения пользователя (`role: "user"`)
- Ответы агента (`role: "assistant"`)

**Что НЕ сохраняется:**
- Факты и знания → идут в LongTermMemory
- Данные плана → идут в WorkingMemory

**Метрики:**
- `Count` — текущее количество записей
- `Last` — последнее сообщение
- `GetRecent(n)` — последние N записей
- `Search(query)` — поиск по содержимому

---

### 2. Рабочая память (WorkingMemory)

**Назначение:** Данные текущей задачи — план, подзадачи, промежуточные результаты.

**Хранение:** `Dictionary<string, WorkingEntry>` — ключ-значение с типизацией.

**Жизненный цикл:** Текущая задача. Очищается при `StartTask(newId)` или `ClearWorking()`.

**Лимит:** Безлимитная (ограничена памятью процесса).

**Что сохраняется:**
- Оригинальный запрос: `Save("request", userMessage, "request")`
- План: `SavePlan(plan)` → сохраняет план, задачи, статусы, финальный ответ
- Результаты подзадач: `Save($"task.{i}.result", task.Result, "task_result")`
- Промежуточные данные: `Save(key, value, type)`

**Что НЕ сохраняется:**
- Сообщения диалога → идут в ShortTermMemory
- Факты → идут в LongTermMemory

**Логика задач:**
- `StartTask(id)` — очищает память, устанавливает текущую задачу
- `CompleteTask(result)` — помечает задачу как завершённую
- `FailTask(reason)` — помечает задачу как проваленную
- `LoadCurrentTaskData()` — загружает данные только текущей задачи

**Метрики:**
- `CurrentTaskId` — идентификатор текущей задачи
- `CurrentTaskStatus` — статус (running/completed/failed)
- `TaskStartedAt` — время начала
- `Count` — количество записей

---

### 3. Долгосрочная память (LongTermMemory)

**Назначение:** Персистентные знания — факты, профиль пользователя, принятые решения.

**Хранение:** `Dictionary<string, Fact>` — ключ-значение с метаданными.

**Жизненный цикл:** Персистентная. Сохраняется в `agent_context.json`, переживает перезапуск.

**Лимит:** 500 фактов (настраивается). При превышении удаляется самый старый.

**Что сохраняется:**
- Факты, извлечённые LLM из диалога: `Save(key, value, "extracted")`
- Явно указанные пользователем факты: `Save(key, value, "user")`
- Мигрированные факты из веток/StickyFacts: `Save(key, value, "migrated")`

**Что НЕ сохраняется:**
- Сообщения диалога → идут в ShortTermMemory
- Данные задачи → идут в WorkingMemory

**Модель факта (Fact):**

| Поле | Описание |
|---|---|
| `Key` | Уникальный ключ (например, "предпочтения.язык") |
| `Value` | Значение факта |
| `Source` | Источник: "user" / "extracted" / "explicit" / "migrated" |
| `CreatedAt` | Время создания |
| `LastReadAt` | Время последнего чтения |
| `ReadCount` | Количество чтений (индикатор релевантности) |

**Поиск релевантных фактов (`FindRelevant`):**

Система скоринга (топ-10):
- Точное совпадение ключа: **+100 баллов**
- Частичное совпадение ключа: **+50 баллов**
- Слово в значении: **+10 баллов**
- Слово в ключе: **+15 баллов**
- Бонус за частоту чтения: **+2 × ReadCount**

**Метрики:**
- `Count` — количество фактов
- `All` — все факты
- `FindRelevant(query)` — поиск по релевантности
- `GetRecentChanges()` — изменения последнего действия

---

## Менеджер памяти (MemoryManager)

Единая точка управления тремя типами памяти.

**Основные методы:**

| Метод | Описание |
|---|---|
| `Save(type, key, value, source)` | Сохранить в указанный тип памяти |
| `Load(type, key)` | Загрузить по ключу |
| `FindRelevant(type, query)` | Найти релевантное |
| `AddToDialogue(role, content)` | Добавить в краткосрочную (диалог) |
| `StartTask(id)` | Начать задачу (сброс рабочей) |
| `CompleteTask(result)` | Завершить задачу |
| `FailTask(reason)` | Провалить задачу |
| `ClearAll()` | Очистить всё |
| `ClearShortTerm()` | Очистить диалог |
| `ClearWorking()` | Очистить задачу |
| `ClearLongTerm()` | Очистить знания |
| `GetStatus()` | Сводка по всем типам |

---

## Потоки данных

```
Пользователь вводит сообщение
        │
        ▼
┌─────────────────────┐
│  ShortTermMemory    │  ← Добавляем сообщение пользователя
│  (краткосрочная)     │
└─────────┬───────────┘
          │
          ▼
┌─────────────────────┐
│  LongTermMemory     │  ← Ищем релевантные факты
│  (долгосрочная)      │
└─────────┬───────────┘
          │
          ▼
   [Простой запрос?]
     ┌────┴────┐
     │         │
    НЕТ       ДА
     │         │
     ▼         ▼
┌────────┐ ┌──────────────┐
│ API    │ │ WorkingMemory│ ← Сохраняем план и результаты
└───┬────┘ └──────┬───────┘
    │             │
    └──────┬──────┘
           │
           ▼
┌─────────────────────┐
│  ShortTermMemory    │  ← Добавляем ответ агента
│  (краткосрочная)     │
└─────────┬───────────┘
          │
          ▼
┌─────────────────────┐
│  LongTermMemory     │  ← Извлекаем и сохраняем новые факты
│  (долгосрочная)      │
└─────────────────────┘
```

---

## Персистентность

### Сохранение (`ContextPersistence.SaveContext`)

Все три типа памяти сериализуются в `agent_context.json`:

```json
{
  "version": 2,
  "savedAt": "2026-09-18T00:00:00Z",
  "history": { "messages": [...] },
  "shortTermMemory": {
    "entries": [...],
    "maxSize": 100
  },
  "workingMemory": {
    "entries": { "key": { "value": "...", "type": "...", ... } },
    "currentTaskId": "plan-20260918...",
    "currentTaskStatus": "completed"
  },
  "memory": {
    "key": { "key": "...", "value": "...", "source": "...", "readCount": 5 }
  },
  "cache": { ... },
  "metrics": { ... },
  "contextManager": { ... }
}
```

### Загрузка (`ContextPersistence.LoadContext`)

При старте:
1. Проверяется наличие `agent_context.json`
2. Восстанавливаются все три типа памяти
3. Факты из веток/StickyFacts мигрируются в LongTermMemory
4. Просроченные записи кэша отфильтровываются

---

## Конфигурация (appsettings.json)

```json
{
  "Memory": {
    "ShortTerm": {
      "MaxSize": 100,
      "DecayEnabled": false,
      "DecayAfterHours": 1
    },
    "Working": {
      "ArchiveEnabled": true,
      "MaxArchiveSize": 10
    },
    "LongTerm": {
      "MaxSize": 500
    }
  }
}
```

Конфигурация применяется при старте в `Program.cs`:

```csharp
var memoryConfig = new MemoryConfig();
configuration.GetSection("Memory").Bind(memoryConfig);

memoryManager.ShortTerm.MaxSize = memoryConfig.ShortTerm.MaxSize;
memoryManager.ShortTerm.DecayEnabled = memoryConfig.ShortTerm.DecayEnabled;
memoryManager.Working.ArchiveEnabled = memoryConfig.Working.ArchiveEnabled;
memoryManager.LongTerm.MaxSize = memoryConfig.LongTerm.MaxSize;
```

---

## Распределение данных: шпаргалка

| Данные | Тип памяти | Метод |
|---|---|---|
| Сообщение пользователя | **ShortTerm** | `MemoryManager.ShortTerm.Add("user", msg)` |
| Ответ агента | **ShortTerm** | `MemoryManager.ShortTerm.Add("assistant", answer)` |
| Оригинальный запрос | **Working** | `MemoryManager.Working.Save("request", msg, "request")` |
| План выполнения | **Working** | `MemoryManager.Working.SavePlan(plan)` |
| Результат подзадачи | **Working** | `MemoryManager.Working.Save($"task.{i}.result", result, "task_result")` |
| Извлечённый факт | **LongTerm** | `MemoryManager.LongTerm.Save(key, value, "extracted")` |
| Явный факт от пользователя | **LongTerm** | `MemoryManager.LongTerm.Save(key, value, "user")` |
| Релевантные факты для контекста | **LongTerm** | `MemoryManager.LongTerm.FindRelevant(query)` |

---

## Расширяемость

Модель памяти спроектирована для расширения:

1. **Приоритеты фактов** ✅ — добавлено поле `Priority` в `Fact`, сортировка при eviction, бонус в скоринге поиска
2. **Decay (забывание)** ✅ — добавлен метод `ApplyDecay()` для ShortTermMemory, пометка старых записей как `IsDecayed`
3. **Архивация рабочей памяти** ✅ — `ArchiveCurrentTask()` сохраняет завершённые задачи, `GetArchivedTask()` восстанавливает
4. **Профиль пользователя** — выделенный раздел LongTermMemory с `source: "profile"` и `priority: Critical`
5. **Векторный поиск** — заменить текстовый скоринг на эмбеддинги

### Новые возможности

#### Приоритеты фактов (FactPriority)

```
Low (0)    — извлечённые автоматически
Normal (1) — факты с нормальной важностью (по умолчанию)
High (2)   — критически важные (профиль, решения)
Critical (3) — никогда не удаляются при eviction
```

При eviction факты с Priority.Critical пропускаются. При поиске приоритет даёт бонус: `score += (int)Priority * 20`.

#### Decay (затухание краткосрочной памяти)

```csharp
memoryManager.ShortTerm.DecayEnabled = true;
memoryManager.ShortTerm.DecayAfterHours = 1;

// Помечает старые записи как IsDecayed
int decayed = memoryManager.ShortTerm.ApplyDecay();

// Удаляет затухшие записи
int removed = memoryManager.ShortTerm.RemoveDecayed();
```

При переполнении сначала удаляются затухшие, затем самые старые.

#### Архивация рабочей памяти

```csharp
// Автоматически архивирует при StartTask
memoryManager.Working.ArchiveEnabled = true;
memoryManager.Working.MaxArchiveSize = 10;

// Ручная архивация
memoryManager.Working.ArchiveCurrentTask();

// Восстановление архива
var archived = memoryManager.Working.GetArchivedTask(taskId);
```
