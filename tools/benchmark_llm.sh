#!/usr/bin/env bash
#
# Benchmark для оптимизации локальной LLM (Ollama + qwen3:14b)
#
# Сравнение:
#   1. Базовая конфигурация (temperature=0.7, maxTokens=0)
#   2. Детерминированная (temperature=0.1, maxTokens=4096, top_k=15)
#   3. Сбалансированная (temperature=0.3, maxTokens=4096, top_k=20)
#   4. Скоростная (temperature=0.7, maxTokens=1024, top_k=50)
#
# Метрики:
#   - Скорость (tok/s — токенов в секунду)
#   - Время до первого токена (TTFT)
#   - Потребление RAM/CPU
#   - Качество ответа (ручная оценка по шкале 1-5)
#

set -euo pipefail

OLLAMA_URL="${OLLAMA_URL:-http://localhost:11434}"
MODEL="${MODEL:-qwen3:14b}"
NUM_RUNS="${NUM_RUNS:-3}"
PROMPT_FILE="${PROMPT_FILE:-}"

# Цвета для вывода
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Тестовые промпты
PROMPTS=(
    "Какие источники данных лучше использовать для микросервиса обработки заказов? Нужно хранить: данные клиентов, заказы, платежи. Сервис должен поддерживать high read throughput и occasional writes. Предложи архитектуру хранения с обоснованием."
    
    "Сравни EF Core vs Dapper для микросервиса с высокой нагрузкой на чтение (10k RPS). Сервис на ASP.NET Core, база PostgreSQL. Какие будут trade-offs?"
    
    "Как реализовать паттерн Saga для распределённой транзакции между микросервисами OrderService и PaymentService на .NET? Оркестрация или хореография?"
    
    "Проектирую API Gateway для микросервисов на .NET. Что использовать: YARP, Ocelot, или кастомный на Minimal APIs? Сравни по критериям: производительность, экосистема, community, гибкость."
    
    "Какие стратегии кэширования применить для микросервиса с PostgreSQL? Нужно кэшировать результаты сложных SQL-запросов. Сравни Redis vs MemoryCache vs IMemoryCache."
)

# Массив для хранения результатов
declare -a RESULTS

log_header() {
    echo ""
    echo -e "${CYAN}═══════════════════════════════════════════════════════════${NC}"
    echo -e "${CYAN}  $1${NC}"
    echo -e "${CYAN}═══════════════════════════════════════════════════════════${NC}"
    echo ""
}

log_section() {
    echo -e "\n${BLUE}── $1 ──${NC}\n"
}

check_prerequisites() {
    # Проверка Ollama
    if ! curl -s "${OLLAMA_URL}/api/tags" > /dev/null 2>&1; then
        echo -e "${RED}❌ Ollama недоступен по ${OLLAMA_URL}${NC}"
        echo "   Запустите: ollama serve"
        exit 1
    fi

    # Проверка модели
    local models
    models=$(curl -s "${OLLAMA_URL}/api/tags" | python3 -c "import json,sys; print('\n'.join(m['name'] for m in json.load(sys.stdin).get('models',[])))" 2>/dev/null)
    
    if ! echo "$models" | grep -q "^${MODEL}$"; then
        echo -e "${YELLOW}⚠️  Модель ${MODEL} не найдена. Доступные модели:${NC}"
        echo "$models"
        echo "   Запустите: ollama pull ${MODEL}"
        exit 1
    fi

    echo -e "${GREEN}✅ Ollama доступен${NC}"
    echo -e "${GREEN}✅ Модель ${MODEL} загружена${NC}"
}

get_system_metrics() {
    # RAM usage
    local ram_total ram_available ram_used_pct
    ram_total=$(free -m | awk '/^Mem:/{print $2}')
    ram_available=$(free -m | awk '/^Mem:/{print $7}')
    ram_used_pct=$(( (ram_total - ram_available) * 100 / ram_total ))

    # CPU usage (sample)
    local cpu_usage
    cpu_usage=$(top -bn1 | grep "Cpu(s)" | awk '{print $2}' 2>/dev/null || echo "0")

    echo "${ram_total}M, ${ram_used_pct}%, ${cpu_usage}%CPU"
}

run_benchmark_single() {
    local temperature="$1"
    local max_tokens="$2"
    local top_k="$3"
    local top_p="$4"
    local repeat_penalty="$5"
    local prompt="$6"
    local run_num="$7"

    # Измеряем RAM до запроса
    local ram_before
    ram_before=$(free -m | awk '/^Mem:/{print $7}')

    # Измеряем время и получаем ответ
    local start_time end_time elapsed response
    start_time=$(date +%s%N)
    
    response=$(curl -s -X POST "${OLLAMA_URL}/api/chat" \
        -H "Content-Type: application/json" \
        -d "{
            \"model\": \"${MODEL}\",
            \"messages\": [
                {\"role\": \"system\", \"content\": \"Ты — архитектор .NET-систем. Отвечай на русском языке. Будь краток и конкретен.\"},
                {\"role\": \"user\", \"content\": \"${prompt}\"}
            ],
            \"stream\": false,
            \"options\": {
                \"temperature\": ${temperature},
                \"num_predict\": ${max_tokens},
                \"top_k\": ${top_k},
                \"top_p\": ${top_p},
                \"repeat_penalty\": ${repeat_penalty}
            }
        }" 2>/dev/null)

    end_time=$(date +%s%N)
    elapsed=$(( (end_time - start_time) / 1000000 )) # ms

    # Парсим ответ
    local content completion_tokens prompt_tokens
    local parsed
    parsed=$(echo "$response" | python3 -c "
import json, sys
try:
    d = json.load(sys.stdin)
    msg = d.get('message', {})
    content = msg.get('content', '')[:100].replace('\n', ' ')
    eval_count = d.get('eval_count', 0)
    prompt_eval_count = d.get('prompt_eval_count', 0)
    # Use TAB as separator to avoid issues with content
    print(f'{content}\t{eval_count}\t{prompt_eval_count}')
except Exception as e:
    print(f'PARSE_ERROR\t0\t0')
" 2>/dev/null)

    completion_tokens=$(echo "$parsed" | cut -f2)
    prompt_tokens=$(echo "$parsed" | cut -f3)
    local response_preview
    response_preview=$(echo "$parsed" | cut -f1)

    # RAM после
    local ram_after
    ram_after=$(free -m | awk '/^Mem:/{print $7}')
    local ram_delta=$(( ram_after - ram_before ))

    # Скорость (токенов/сек)
    local speed=0
    if [ "$completion_tokens" -gt 0 ] 2>/dev/null && [ "$elapsed" -gt 0 ] 2>/dev/null; then
        speed=$(echo "scale=1; $completion_tokens * 1000 / $elapsed" | bc 2>/dev/null || echo "0")
    fi

    # TTFT (приблизительно — Ollama не отдаёт TTFT в non-stream режиме)
    local ttft_approx=$(( elapsed / 2 ))

    echo "${temperature}|${max_tokens}|${top_k}|${top_p}|${repeat_penalty}|${elapsed}|${completion_tokens}|${prompt_tokens}|${speed}|${ttft_approx}|${ram_delta}"
}

run_full_benchmark() {
    log_header "BENCHMARK: Оптимизация локальной LLM"
    echo "Модель: ${MODEL}"
    echo "Количество запусков на конфигурацию: ${NUM_RUNS}"
    echo "Тестовых промптов: ${#PROMPTS[@]}"
    echo "Система: $(lscpu | grep 'Model name' | cut -d: -f2 | xargs)"
    echo "RAM: $(free -h | awk '/^Mem:/{print $2" total, "$7" available"}')"
    echo ""

    # Конфигурации для тестирования
    declare -a CONFIGS=(
        "0.7|0|50|0.99|1.0|DEFAULT (базовая)"
        "0.1|4096|15|0.90|1.10|DETERMINISTIC (требования)"
        "0.3|4096|20|0.95|1.05|BALANCED (архитектура)"
        "0.5|2048|30|0.95|1.05|MID (компромисс)"
    )

    local all_results_file="/tmp/llm_benchmark_$(date +%Y%m%d_%H%M%S).csv"
    echo "config,temperature,max_tokens,top_k,top_p,repeat_penalty,run,prompt_idx,time_ms,completion_tokens,prompt_tokens,speed_tok_s,ram_delta_mb,preview" > "$all_results_file"

    for config in "${CONFIGS[@]}"; do
        IFS='|' read -r temp max_tk tk tp rp label <<< "$config"
        
        log_section "Конфигурация: ${label} (temp=${temp}, max=${max_tk}, top_k=${tk})"

        local config_results=()
        
        for prompt_idx in "${!PROMPTS[@]}"; do
            local prompt="${PROMPTS[$prompt_idx]}"
            echo -e "  ${YELLOW}Промпт #${prompt_idx}: ${prompt:0:80}...${NC}"

            local run_results=()
            for run in $(seq 1 $NUM_RUNS); do
                echo -n "    run ${run}/${NUM_RUNS}... "
                
                local result
                result=$(run_benchmark_single "$temp" "$max_tk" "$tk" "$tp" "$rp" "$prompt" "$run")
                
                IFS='|' read -r _t _mt _tk _tp _rp time_ms comp_tk prom_tk speed ram_delta _ <<< "$result"
                
                echo -e "${GREEN}${time_ms}ms${NC} (${speed} tok/s, ${comp_tk} tok out)"
                
                run_results+=("${result}")
                echo "${label},${temp},${max_tk},${tk},${tp},${rp},${run},${prompt_idx},${time_ms},${comp_tk},${prom_tk},${speed},${ram_delta},N/A" >> "$all_results_file"
            done

            # Среднее по runs
            local total_time=0 total_tok=0
            for r in "${run_results[@]}"; do
                IFS='|' read -r _ _ _ _ _ t _ ct _ s _ _ <<< "$r"
                total_time=$((total_time + t))
                total_tok=$((total_tok + ct))
            done
            local avg_time=$((total_time / NUM_RUNS))
            local avg_speed=$(echo "scale=1; $total_tok * 1000 / $total_time" | bc)
            echo -e "  → ${GREEN}Среднее: ${avg_time}ms, ${avg_speed} tok/s${NC}"
        done
        echo ""
    done

    echo ""
    log_header "РЕЗУЛЬТАТЫ СОХРАНЕНЫ"
    echo "CSV файл: ${all_results_file}"
    echo ""
    echo "Для анализа запустите:"
    echo "  python3 analyze_benchmark.py ${all_results_file}"
}

show_help() {
    echo "Usage: $0 [command] [options]"
    echo ""
    echo "Commands:"
    echo "  run       Run full benchmark (default)"
    echo "  quick     Quick benchmark (1 run, 2 prompts)"
    echo "  check     Check prerequisites only"
    echo ""
    echo "Environment variables:"
    echo "  OLLAMA_URL  Ollama API URL (default: http://localhost:11434)"
    echo "  MODEL       Model name (default: qwen3:14b)"
    echo "  NUM_RUNS    Runs per config (default: 3)"
    echo ""
    echo "Examples:"
    echo "  $0 run"
    echo "  MODEL=qwen3:8b NUM_RUNS=1 $0 quick"
}

# Main
case "${1:-run}" in
    run)
        check_prerequisites
        run_full_benchmark
        ;;
    quick)
        check_prerequisites
        NUM_RUNS=1
        # Тестовые промпты для quick mode
        PROMPTS=(
            "Сравни Redis и RabbitMQ для микросервисов на .NET. Когда что использовать?"
            "Какой формат API выбрать: REST, gRPC, GraphQL для внутреннего межсервисного общения?"
        )
        run_full_benchmark
        ;;
    check)
        check_prerequisites
        ;;
    help|--help|-h)
        show_help
        ;;
    *)
        echo "Unknown command: $1"
        show_help
        exit 1
        ;;
esac
