#!/usr/bin/env python3
"""
Анализ результатов benchmark LLM.
Читает CSV-файл и генерирует сравнительную таблицу + рекомендации.

Usage: python3 analyze_benchmark.py <csv_file>
"""

import sys
import csv
from collections import defaultdict
from datetime import datetime


def analyze(csv_file: str):
    # Чтение данных
    configs = defaultdict(lambda: {
        "times": [], "tokens_out": [], "tokens_in": [],
        "speeds": [], "ram_deltas": [], "runs": 0
    })

    with open(csv_file, "r") as f:
        reader = csv.DictReader(f)
        for row in reader:
            label = row["config"]
            configs[label]["times"].append(int(row["time_ms"]))
            configs[label]["tokens_out"].append(int(row["completion_tokens"]))
            configs[label]["tokens_in"].append(int(row["prompt_tokens"]))
            try:
                configs[label]["speeds"].append(float(row["speed_tok_s"]))
            except (ValueError, KeyError):
                pass
            try:
                configs[label]["ram_deltas"].append(int(row["ram_delta_mb"]))
            except (ValueError, KeyError):
                pass
            configs[label]["runs"] += 1

    if not configs:
        print("❌ Нет данных для анализа")
        return

    # Расчёт статистики
    print("\n" + "=" * 80)
    print("РЕЗУЛЬТАТЫ BENCHMARK")
    print("=" * 80)

    stats = {}
    for label, data in sorted(configs.items()):
        times = data["times"]
        speeds = data["speeds"]
        tokens_out = data["tokens_out"]
        tokens_in = data["tokens_in"]
        ram = data["ram_deltas"]

        avg_time = sum(times) / len(times) if times else 0
        min_time = min(times) if times else 0
        max_time = max(times) if times else 0
        p50_time = sorted(times)[len(times) // 2] if times else 0

        avg_speed = sum(speeds) / len(speeds) if speeds else 0
        avg_tokens_out = sum(tokens_out) / len(tokens_out) if tokens_out else 0
        avg_tokens_in = sum(tokens_in) / len(tokens_in) if tokens_in else 0
        avg_ram = sum(ram) / len(ram) if ram else 0

        stats[label] = {
            "avg_time": avg_time,
            "min_time": min_time,
            "max_time": max_time,
            "p50_time": p50_time,
            "avg_speed": avg_speed,
            "avg_tokens_out": avg_tokens_out,
            "avg_tokens_in": avg_tokens_in,
            "avg_ram": avg_ram,
            "runs": data["runs"],
        }

    # Таблица сравнения
    print(f"\n{'Конфигурация':<35} {'Среднее время':>12} {'Скорость':>10} {'Токенов out':>12} {'RAM delta':>10}")
    print("-" * 85)

    for label, s in stats.items():
        short_label = label.split(" (")[0] if " (" in label else label
        print(f"{short_label:<35} {s['avg_time']:>10.0f}ms {'':>2} {s['avg_speed']:>8.1f} tok/s {s['avg_tokens_out']:>10.0f} {s['avg_ram']:>8.0f}MB")

    # Детальная статистика
    print("\n" + "=" * 80)
    print("ДЕТАЛЬНАЯ СТАТИСТИКА")
    print("=" * 80)

    for label, s in sorted(stats.items()):
        print(f"\n📊 {label}")
        print(f"   Запусков: {s['runs']}")
        print(f"   Время: среднее={s['avg_time']:.0f}ms, мин={s['min_time']}ms, макс={s['max_time']}ms, p50={s['p50_time']}ms")
        print(f"   Скорость: {s['avg_speed']:.1f} tok/s")
        print(f"   Токены: in={s['avg_tokens_in']:.0f}, out={s['avg_tokens_out']:.0f}")
        print(f"   RAM: delta={s['avg_ram']:.0f}MB")

    # Рекомендации
    print("\n" + "=" * 80)
    print("РЕКОМЕНДАЦИИ")
    print("=" * 80)

    if stats:
        # fastest = lowest avg_time
        fastest = min(stats.items(), key=lambda x: x[1]["avg_time"])
        highest_speed = max(stats.items(), key=lambda x: x[1]["avg_speed"])
        most_efficient = min(
            stats.items(),
            key=lambda x: x[1]["avg_time"] / max(x[1]["avg_tokens_out"], 1)
        )

        print(f"\n🚀 Самая быстрая конфигурация: {fastest[0]}")
        print(f"   Среднее время: {fastest[1]['avg_time']:.0f}ms")

        print(f"\n⚡ Максимальная скорость: {highest_speed[0]}")
        print(f"   {highest_speed[1]['avg_speed']:.1f} tok/s")

        print(f"\n📈 Наиболее эффективная (время/токен): {most_efficient[0]}")
        print(f"   {most_efficient[1]['avg_time'] / max(most_efficient[1]['avg_tokens_out'], 1):.2f}ms на токен")

        # Рекомендации по кейсам
        print(f"\n{'='*80}")
        print("РЕКОМЕНДУЕМЫЕ ПРОФИЛИ ПО КЕЙСАМ")
        print(f"{'='*80}")

        print(f"""
📋 Сбор требований (нужна точность и детерминизм):
   → Используйте профиль DETERMINISTIC
   → temperature=0.1, max_tokens=4096, top_k=15, top_p=0.9, repeat_penalty=1.1
   → Приоритет: качество ответа > скорость

🏗️  Архитектурные дискуссии (нужен баланс):
   → Используйте профиль BALANCED
   → temperature=0.3, max_tokens=4096, top_k=20, top_p=0.95, repeat_penalty=1.05
   → Приоритет: баланс качества и скорости

⚡ Быстрые ответы (справка, короткие вопросы):
   → Используйте профиль SPEED
   → temperature=0.5, max_tokens=1024, top_k=30, top_p=0.95, repeat_penalty=1.05
   → Приоритет: скорость > глубина ответа

🔬 Планирование/валидация (FSM):
   → temperature=0.1, max_tokens=2048, top_k=10, top_p=0.85
   → Приоритет: максимальная детерминированность
""")

    # Квантование
    print(f"\n{'='*80}")
    print("О КВАНТОВАНИИ")
    print(f"{'='*80}")
    print("""
📦 Текущая модель qwen3:14b уже квантована в Q4_K_M (9.3 GB).

Доступные варианты квантования для 14B моделей:
  Q2_K     ~5-6 GB   — максимальная скорость, максимальная потеря качества
  Q3_K_M   ~7-8 GB   — хороший компромисс
  Q4_K_M   ~9-10 GB  — текущая модель, хороший баланс ← ВЫБОР ПО УМОЛЧАНИЮ
  Q5_K_M   ~11-12 GB — чуть лучше качество, больше RAM
  Q6_K     ~13-14 GB — максимальное качество, много RAM

Для CPU-инференса (Ryzen 5 5600H, без GPU):
  → Q4_K_M — оптимальный выбор (достаточное качество, разумное потребление RAM)
  → Q3_K_M — если RAM в обрез (<12 GB свободно)
  → Q5_K_M — если качество важнее скорости и есть 16+ GB RAM

Для сравнения квантований:
  ollama pull qwen3:14b-q2_k    # быстрая, менее точная
  ollama pull qwen3:14b-q3_k_m  # баланс
  ollama pull qwen3:14b-q5_k_m  # точная, медленнее
  ollama pull qwen3:14b-q6_k    # самая точная, самая медленная
""")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python3 analyze_benchmark.py <csv_file>")
        print("")
        print("CSV file is generated by benchmark_llm.sh")
        sys.exit(1)

    csv_file = sys.argv[1]
    try:
        analyze(csv_file)
    except FileNotFoundError:
        print(f"❌ Файл не найден: {csv_file}")
        sys.exit(1)
    except Exception as e:
        print(f"❌ Ошибка: {e}")
        sys.exit(1)
